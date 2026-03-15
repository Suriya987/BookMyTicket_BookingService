using BookingService.API.Infrastructure.Http;
using BookingService.API.Infrastructure.Kafka;
using BookingService.API.Infrastructure.Redis;
using BookingService.API.Models;
using BookingService.API.Models.DTOs;
using BookingService.API.Repositories;
using StackExchange.Redis;
using System.Text.Json;
//using InventoryService.API.Infrastructure.Redis;

namespace BookingService.API.Services;

public interface IBookingService
{
    Task<CreateBookingResponse>        CreateBookingAsync(Guid userId, CreateBookingRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<BookingResponse?>             GetBookingAsync(Guid bookingId, Guid userId, CancellationToken ct = default);
    Task<IEnumerable<BookingResponse>> GetMyBookingsAsync(Guid userId, CancellationToken ct = default);
    Task<CancelBookingResponse>        CancelBookingAsync(Guid bookingId, Guid userId, CancelBookingRequest request, CancellationToken ct = default);
}

internal class SeatCacheEntry
{
    public Guid ShowSeatId { get; set; }
    public Guid ShowId { get; set; }
    public string Status { get; set; } = default!;
    public string RowVersion { get; set; } = default!;
}

public class BookingService : IBookingService
{
    private readonly IBookingRepository    _repo;
    private readonly IRedisLockService     _redisLock;
    private readonly IDatabase             _redisDb;
    private readonly IInventoryClient      _inventoryClient;
    private readonly IPaymentClient        _paymentClient;
    private readonly IKafkaPublisherService _publisher;
    private readonly ILogger<BookingService> _logger;

    // Redis key patterns
    private const string LockPrefix        = "lock:show:{0}:seat:{1}";       // lock:show:showId:seat:showSeatId
    private const string SeatCachePrefix   = "seat:show:{0}:seat:{1}";       // seat status cache
    private const string IdempotencyPrefix = "idempotency:booking:{0}";      // dedup
    private const string BookedPrefix      = "booked:show:{0}:seat:{1}";     // permanent booked flag

    private static readonly TimeSpan SeatLockTtl    = TimeSpan.FromMinutes(7);
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    public BookingService(
        IBookingRepository      repo,
        IRedisLockService       redisLock,
        IConnectionMultiplexer  redis,
        IInventoryClient        inventoryClient,
        IPaymentClient          paymentClient,
        IKafkaPublisherService  publisher,
        ILogger<BookingService> logger)
    {
        _repo            = repo;
        _redisLock       = redisLock;
        _redisDb         = redis.GetDatabase();
        _inventoryClient = inventoryClient;
        _paymentClient   = paymentClient;
        _publisher       = publisher;
        _logger          = logger;
    }

    public async Task<CreateBookingResponse> CreateBookingAsync(
        Guid userId,
        CreateBookingRequest request,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "CreateBooking → user={UserId} show={ShowId} seats={SeatCount} key={Key}",
            userId, request.ShowId, request.ShowSeatIds.Count, idempotencyKey);

        // ── Step 1: Idempotency check ─────────────────────────────────────
        var idempotencyValue = await _redisDb.StringGetAsync(
            string.Format(IdempotencyPrefix, idempotencyKey));

        if (idempotencyValue.HasValue)
        {
            _logger.LogInformation("Idempotent replay key={Key}", idempotencyKey);
            return new CreateBookingResponse
            {
                BookingId = Guid.Parse(idempotencyValue!),
                Status    = BookingStatuses.Pending
            };
        }

        // ── Step 2: Check Redis booked flag for each seat ─────────────────
        // Fastest check — no DB, no HTTP
        // If any seat is permanently booked → reject immediately
        foreach (var showSeatId in request.ShowSeatIds)
        {
            var bookedKey = string.Format(BookedPrefix, request.ShowId, showSeatId);
            var isBooked  = await _redisDb.KeyExistsAsync(bookedKey);
            if (isBooked)
            {
                _logger.LogWarning("Seat {ShowSeatId} already booked (Redis)", showSeatId);
                throw new SeatNotAvailableException(showSeatId, SeatStatuses.Booked);
            }
        }

        // ── Step 3: Check seat status from Redis cache ────────────────────
        // If cache miss → will be handled in Step 5 via Inventory HTTP call
        //foreach (var showSeatId in request.ShowSeatIds)
        //{
        //    var cacheKey    = string.Format(SeatCachePrefix, request.ShowId, showSeatId);
        //    var cachedStatus = await _redisDb.StringGetAsync(cacheKey);
        //    if (cachedStatus.HasValue && cachedStatus != SeatStatuses.Available)
        //    {
        //        _logger.LogWarning(
        //            "Seat {ShowSeatId} is {Status} in Redis cache", showSeatId, cachedStatus);
        //        throw new SeatNotAvailableException(showSeatId, cachedStatus!);
        //    }
        //}
        foreach (var showSeatId in request.ShowSeatIds)
        {
            var cacheKey = string.Format(SeatCachePrefix, request.ShowId, showSeatId);
            var cachedValue = await _redisDb.StringGetAsync(cacheKey);

            //if (cachedValue.HasValue)
            //{
            //    var cached = JsonSerializer.Deserialize<SeatCacheEntry>(cachedValue!,
            //        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            //    if (cached?.Status != null && cached.Status != SeatStatuses.Available)
            //    {
            //        _logger.LogWarning(
            //            "Seat {ShowSeatId} is {Status} in Redis cache",
            //            showSeatId, cached.Status);
            //        throw new SeatNotAvailableException(showSeatId, cached.Status);
            //    }
            //}
            if (cachedValue.HasValue)
            {
                string? status = null;

                // Try JSON first
                try
                {
                    var cached = JsonSerializer.Deserialize<SeatCacheEntry>(cachedValue!,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    status = cached?.Status;
                }
                catch
                {
                    // Plain string value e.g. "AVAILABLE", "HELD", "BOOKED"
                    status = cachedValue.ToString();
                }

                if (status != null && status != SeatStatuses.Available)
                {
                    _logger.LogWarning(
                        "Seat {ShowSeatId} is {Status} in Redis cache",
                        showSeatId, status);
                    throw new SeatNotAvailableException(showSeatId, status);
                }
            }
        }

        // ── Step 4: Acquire Redis locks — ALL seats or NONE ───────────────
        // If any lock fails → release all acquired → 409
        var acquiredLocks = new Dictionary<Guid, string>();
        try
        {
            foreach (var showSeatId in request.ShowSeatIds)
            {
                var lockKey   = string.Format(LockPrefix, request.ShowId, showSeatId);
                var lockToken = await _redisLock.AcquireAsync(lockKey, SeatLockTtl, ct);

                if (lockToken is null)
                {
                    _logger.LogWarning(
                        "Could not acquire lock for seat {ShowSeatId} — releasing all", showSeatId);
                    throw new SeatCurrentlyLockedException(showSeatId);
                }

                acquiredLocks[showSeatId] = lockToken;
            }

            // ── Step 5: Re-check seat status via Inventory HTTP ───────────
            // Inside lock — authoritative check
            // Gets current status + RowVersion for each seat
            var seatInfos = await _inventoryClient.GetShowSeatsAsync(request.ShowSeatIds, ct);

            foreach (var seatInfo in seatInfos)
            {
                if (seatInfo.Status != SeatStatuses.Available)
                {
                    _logger.LogWarning(
                        "Seat {ShowSeatId} is {Status} in Inventory", seatInfo.ShowSeatId, seatInfo.Status);
                    throw new SeatNotAvailableException(seatInfo.ShowSeatId, seatInfo.Status);
                }

                // Store version in Redis — used for optimistic lock check after payment
                var versionKey = $"version:showseat:{seatInfo.ShowSeatId}";
                await _redisDb.StringSetAsync(versionKey, seatInfo.Version, SeatLockTtl);
            }

            // ── Step 6: INSERT Booking (PENDING) ──────────────────────────
            var booking = new Booking
            {
                BookingId      = Guid.NewGuid(),
                UserId         = userId,
                ShowId         = request.ShowId,
                ShowSeatIds    = JsonSerializer.Serialize(request.ShowSeatIds),
                TotalAmount    = request.TotalAmount,
                Status         = BookingStatuses.Pending,
                IdempotencyKey = idempotencyKey,
                HeldUntil      = DateTimeOffset.UtcNow.Add(SeatLockTtl),
                CreatedAt      = DateTimeOffset.UtcNow,
                UpdatedAt      = DateTimeOffset.UtcNow
            };

            await _repo.AddAsync(booking, ct);

            // Cache idempotency key so retries return same bookingId
            await _redisDb.StringSetAsync(
                string.Format(IdempotencyPrefix, idempotencyKey),
                booking.BookingId.ToString(),
                IdempotencyTtl);

            // ── Step 7: Resolve card details ──────────────────────────────
            string cardNumber, cvv, cardType = "UNKNOWN";
            int expiryMonth, expiryYear;
            Guid? cardInfoId = null;

            if (request.CardInfoId.HasValue)
            {
                // Use saved card — get details from CardInfo table
                // (In real app fetch from DB — simplified here)
                cardInfoId  = request.CardInfoId;
                cardNumber  = request.CardNumber!;
                cvv         = request.Cvv!;
                expiryMonth = request.ExpiryMonth!.Value;
                expiryYear  = request.ExpiryYear!.Value;
                cardType    = request.CardType ?? "UNKNOWN";
            }
            else
            {
                // One-time card
                cardNumber  = request.CardNumber!;
                cvv         = request.Cvv!;
                expiryMonth = request.ExpiryMonth!.Value;
                expiryYear  = request.ExpiryYear!.Value;
                cardType    = request.CardType ?? "UNKNOWN";
            }

            // ── Step 8: HTTP → Stripe Payment ─────────────────────────────
            // Redis locks STILL held during payment (7 min TTL)
            // No other request can touch these seats while payment processes
            _logger.LogInformation(
                "Calling Stripe for booking {BookingId} amount={Amount}",
                booking.BookingId, request.TotalAmount);

            var paymentResult = await _paymentClient.ProcessPaymentAsync(
                booking.BookingId, userId,
                request.TotalAmount,
                cardNumber, cvv, expiryMonth, expiryYear, ct);

            // ── Step 9a: Payment FAILED ───────────────────────────────────
            if (!paymentResult.Success)
            {
                _logger.LogWarning(
                    "Payment failed for booking {BookingId}: {Reason}",
                    booking.BookingId, paymentResult.FailureReason);

                booking.Status        = BookingStatuses.Failed;
                booking.FailureReason = paymentResult.FailureReason;
                booking.UpdatedAt     = DateTimeOffset.UtcNow;
                await _repo.UpdateAsync(booking, ct);

                // Record payment attempt
                await _repo.AddPaymentProcessingAsync(new PaymentProcessing
                {
                    ProcessingId          = Guid.NewGuid(),
                    UserId                = userId,
                    BookingId             = booking.BookingId,
                    CardInfoId            = cardInfoId,
                    Amount                = request.TotalAmount,
                    Status                = "FAILED",
                    FailureReason         = paymentResult.FailureReason,
                    PaymentProvider       = "STRIPE",
                    ProviderTransactionId = paymentResult.ProviderTransactionId,
                    ProcessedAt           = DateTimeOffset.UtcNow
                }, ct);

                // Reset seat cache to AVAILABLE
                foreach (var showSeatId in request.ShowSeatIds)
                {
                    var cacheKey = string.Format(SeatCachePrefix, request.ShowId, showSeatId);
                    await _redisDb.KeyDeleteAsync(cacheKey);
                }

                try
                {
                    await _publisher.PublishBookingFailedAsync(
                        booking.BookingId, userId,
                        request.ShowSeatIds, paymentResult.FailureReason ?? "Payment failed", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Kafka publish failed");
                }

                throw new PaymentFailedException(booking.BookingId, paymentResult.FailureReason);
            }

            // ── Step 9b: Payment SUCCESS ──────────────────────────────────
            _logger.LogInformation(
                "Payment succeeded for booking {BookingId} txn={TxnId}",
                booking.BookingId, paymentResult.ProviderTransactionId);

            booking.Status    = BookingStatuses.Confirmed;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            await _repo.UpdateAsync(booking, ct);

            // Record successful payment
            await _repo.AddPaymentProcessingAsync(new PaymentProcessing
            {
                ProcessingId          = Guid.NewGuid(),
                UserId                = userId,
                BookingId             = booking.BookingId,
                CardInfoId            = cardInfoId,
                Amount                = request.TotalAmount,
                Status                = "SUCCESS",
                PaymentProvider       = "STRIPE",
                ProviderTransactionId = paymentResult.ProviderTransactionId,
                ProcessedAt           = DateTimeOffset.UtcNow
            }, ct);

            // Increment card usage count if saved card used
            if (cardInfoId.HasValue)
                await _repo.IncrementCardUsedCountAsync(cardInfoId.Value, ct);

            // ── Step 10: Set Redis booked flag (permanent, no TTL) ────────
            // Fast-path for future availability checks
            // Next request for same seat → hits Redis → BOOKED → 409
            foreach (var showSeatId in request.ShowSeatIds)
            {
                var bookedKey = string.Format(BookedPrefix, request.ShowId, showSeatId);
                await _redisDb.StringSetAsync(bookedKey, booking.BookingId.ToString());

                var cacheKey = string.Format(SeatCachePrefix, request.ShowId, showSeatId);
                await _redisDb.StringSetAsync(cacheKey,
                                                    JsonSerializer.Serialize(new SeatCacheEntry
                                                    {
                                                        ShowSeatId = showSeatId,
                                                        ShowId = request.ShowId,
                                                        Status = SeatStatuses.Booked,
                                                        RowVersion = ""
                                                    }));
            }

            // ── Step 11: Publish booking.confirmed → Kafka ────────────────
            // InventoryService consumes → marks ShowSeat as BOOKED in its DB
            // NotificationService consumes → sends email/SMS
            try
            {
                await _publisher.PublishBookingConfirmedAsync(
                    booking.BookingId, userId, request.ShowId,
                    request.ShowSeatIds, request.TotalAmount, ct);
            }
            catch (Exception ex)
            {
                // Kafka not running locally — log and continue
                // Booking is already CONFIRMED in DB and Redis
                _logger.LogWarning(ex, "Kafka publish failed — booking still confirmed");
            }

            _logger.LogInformation("Booking {BookingId} CONFIRMED", booking.BookingId);

            return new CreateBookingResponse
            {
                BookingId = booking.BookingId,
                Status    = BookingStatuses.Confirmed
            };
        }
        finally
        {
            // ── Always release all acquired locks ─────────────────────────
            foreach (var (showSeatId, lockToken) in acquiredLocks)
            {
                var lockKey = string.Format(LockPrefix, request.ShowId, showSeatId);
                await _redisLock.ReleaseAsync(lockKey, lockToken);
            }
        }
    }

    public async Task<BookingResponse?> GetBookingAsync(
        Guid bookingId, Guid userId, CancellationToken ct = default)
    {
        var booking = await _repo.GetByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != userId) return null;
        return MapToResponse(booking);
    }

    public async Task<IEnumerable<BookingResponse>> GetMyBookingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var bookings = await _repo.GetByUserIdAsync(userId, ct);
        return bookings.Select(MapToResponse);
    }

    public async Task<CancelBookingResponse> CancelBookingAsync(
        Guid bookingId, Guid userId,
        CancelBookingRequest request,
        CancellationToken ct = default)
    {
        var booking = await _repo.GetByIdAsync(bookingId, ct)
            ?? throw new BookingNotFoundException(bookingId);

        if (booking.UserId != userId)
            throw new UnauthorizedBookingException(bookingId, userId);

        if (booking.Status == BookingStatuses.Cancelled)
            throw new BookingAlreadyCancelledException(bookingId);

        if (booking.Status == BookingStatuses.Failed)
            throw new InvalidBookingStateException("Cannot cancel a failed booking");

        booking.Status        = BookingStatuses.Cancelled;
        booking.FailureReason = request.Reason;
        booking.UpdatedAt     = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(booking, ct);

        // Deserialize ShowSeatIds from JSON
        var showSeatIds = JsonSerializer.Deserialize<List<Guid>>(booking.ShowSeatIds)
            ?? new List<Guid>();

        // Remove booked flags from Redis — seats available again
        foreach (var showSeatId in showSeatIds)
        {
            await _redisDb.KeyDeleteAsync(
                string.Format(BookedPrefix, booking.ShowId, showSeatId));
            await _redisDb.StringSetAsync(
                string.Format(SeatCachePrefix, booking.ShowId, showSeatId),
                SeatStatuses.Available);
        }

        await _publisher.PublishBookingCancelledAsync(
            booking.BookingId, userId, showSeatIds, request.Reason, ct);

        _logger.LogInformation("Booking {BookingId} cancelled", bookingId);

        return new CancelBookingResponse
        {
            BookingId = booking.BookingId,
            Status    = BookingStatuses.Cancelled
        };
    }

    private static BookingResponse MapToResponse(Booking b) => new()
    {
        BookingId    = b.BookingId,
        UserId       = b.UserId,
        ShowId       = b.ShowId,
        ShowSeatIds  = JsonSerializer.Deserialize<List<Guid>>(b.ShowSeatIds) ?? new(),
        TotalAmount  = b.TotalAmount,
        Status       = b.Status,
        FailureReason = b.FailureReason,
        CreatedAt    = b.CreatedAt,
        UpdatedAt    = b.UpdatedAt
    };
}

// ── Domain Exceptions ─────────────────────────────────────────────────────────
public class SeatNotAvailableException(Guid showSeatId, string status)
    : Exception($"Seat {showSeatId} is not available. Current status: {status}.");

public class SeatCurrentlyLockedException(Guid showSeatId)
    : Exception($"Seat {showSeatId} is currently being booked. Try again shortly.");

public class PaymentFailedException(Guid bookingId, string? reason)
    : Exception($"Payment failed for booking {bookingId}: {reason}.");

public class BookingNotFoundException(Guid bookingId)
    : Exception($"Booking {bookingId} not found.");

public class UnauthorizedBookingException(Guid bookingId, Guid userId)
    : Exception($"User {userId} cannot modify booking {bookingId}.");

public class BookingAlreadyCancelledException(Guid bookingId)
    : Exception($"Booking {bookingId} is already cancelled.");

public class InvalidBookingStateException(string message)
    : Exception(message);
