using BookingService.API.Data;
using BookingService.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingService.API.Repositories;

public interface IBookingRepository
{
    Task<Booking?>             GetByIdAsync(Guid bookingId, CancellationToken ct = default);
    Task<Booking?>             GetByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<IEnumerable<Booking>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool>                 ExistsByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task                       AddAsync(Booking booking, CancellationToken ct = default);
    Task                       UpdateAsync(Booking booking, CancellationToken ct = default);
    Task                       AddPaymentProcessingAsync(PaymentProcessing payment, CancellationToken ct = default);
    Task                       IncrementCardUsedCountAsync(Guid cardInfoId, CancellationToken ct = default);
}

public class BookingRepository : IBookingRepository
{
    private readonly BookingDbContext _db;

    public BookingRepository(BookingDbContext db) => _db = db;

    public async Task<Booking?> GetByIdAsync(Guid bookingId, CancellationToken ct = default)
        => await _db.Bookings
            .Include(b => b.PaymentProcessings)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);

    public async Task<Booking?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
        => await _db.Bookings
            .FirstOrDefaultAsync(b => b.IdempotencyKey == key, ct);

    public async Task<IEnumerable<Booking>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Bookings
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> ExistsByIdempotencyKeyAsync(string key, CancellationToken ct = default)
        => await _db.Bookings
            .AnyAsync(b => b.IdempotencyKey == key, ct);

    public async Task AddAsync(Booking booking, CancellationToken ct = default)
    {
        await _db.Bookings.AddAsync(booking, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Booking booking, CancellationToken ct = default)
    {
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Bookings.Update(booking);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddPaymentProcessingAsync(PaymentProcessing payment, CancellationToken ct = default)
    {
        await _db.PaymentProcessings.AddAsync(payment, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task IncrementCardUsedCountAsync(Guid cardInfoId, CancellationToken ct = default)
    {
        var card = await _db.CardInfos.FindAsync(new object[] { cardInfoId }, ct);
        if (card is null) return;
        card.UsedCount++;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
