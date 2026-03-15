namespace BookingService.API.Models.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

public class CreateBookingRequest
{
    public Guid        ShowId      { get; set; }
    public List<Guid>  ShowSeatIds { get; set; } = new();  // one or more seats
    public decimal     TotalAmount { get; set; }
    public Guid?       CardInfoId  { get; set; }            // saved card (optional)

    // Card details — used if CardInfoId is null (one-time card)
    public string?     CardNumber  { get; set; }
    public string?     Cvv         { get; set; }
    public string?     CardType    { get; set; }
    public string?     CardHolderName { get; set; }
    public int?        ExpiryMonth { get; set; }
    public int?        ExpiryYear  { get; set; }

    // Save this card for future use?
    public bool        SaveCard    { get; set; } = false;
}

public class CancelBookingRequest
{
    public string Reason { get; set; } = default!;
}

// ── Responses ─────────────────────────────────────────────────────────────────

public class CreateBookingResponse
{
    public Guid   BookingId { get; set; }
    public string Status    { get; set; } = default!;
}

public class BookingResponse
{
    public Guid    BookingId    { get; set; }
    public Guid    UserId       { get; set; }
    public Guid    ShowId       { get; set; }
    public List<Guid> ShowSeatIds { get; set; } = new();
    public decimal TotalAmount  { get; set; }
    public string  Status       { get; set; } = default!;
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CancelBookingResponse
{
    public Guid   BookingId { get; set; }
    public string Status    { get; set; } = default!;
}

public class ApiErrorResponse
{
    public string Error   { get; set; } = default!;
    public string Code    { get; set; } = default!;
    public string TraceId { get; set; } = default!;
}

// ── Status Constants ──────────────────────────────────────────────────────────

public static class BookingStatuses
{
    public const string Pending   = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Failed    = "FAILED";
    public const string Cancelled = "CANCELLED";
}

public static class SeatStatuses
{
    public const string Available = "AVAILABLE";
    public const string Held      = "HELD";
    public const string Booked    = "BOOKED";
}
