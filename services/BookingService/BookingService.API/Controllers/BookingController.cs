using BookingService.API.Models.DTOs;
using BookingService.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BookingService.API.Controllers;

[ApiController]
[Route("v1/bookings")]
//[Authorize]
[Produces("application/json")]
public class BookingController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingController(IBookingService bookingService)
        => _bookingService = bookingService;

    // POST v1/bookings
    // Header: Idempotency-Key: {uuid}
    [HttpPost]
    [ProducesResponseType(typeof(CreateBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse),      StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse),      StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiErrorResponse),      StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateBookingResponse>> CreateBooking(
        [FromBody]   CreateBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(Error("MISSING_IDEMPOTENCY_KEY",
                "Idempotency-Key header is required"));

        if (request.ShowSeatIds is null || !request.ShowSeatIds.Any())
            return BadRequest(Error("MISSING_SEATS",
                "At least one ShowSeatId is required"));

        if (request.CardInfoId is null &&
            (string.IsNullOrWhiteSpace(request.CardNumber) ||
             string.IsNullOrWhiteSpace(request.Cvv)))
            return BadRequest(Error("MISSING_CARD",
                "Either CardInfoId or card details are required"));

        var result = await _bookingService.CreateBookingAsync(
            GetUserId(), request, idempotencyKey, ct);

        return Ok(result);
    }

    // GET v1/bookings/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingResponse>> GetBooking(
        Guid id, CancellationToken ct)
    {
        var booking = await _bookingService.GetBookingAsync(id, GetUserId(), ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    // GET v1/bookings
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BookingResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> GetMyBookings(
        CancellationToken ct)
        => Ok(await _bookingService.GetMyBookingsAsync(GetUserId(), ct));

    // DELETE v1/bookings/{id}
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(CancelBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse),      StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse),      StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CancelBookingResponse>> CancelBooking(
        Guid id,
        [FromBody] CancelBookingRequest request,
        CancellationToken ct)
        => Ok(await _bookingService.CancelBookingAsync(id, GetUserId(), request, ct));

    private Guid GetUserId()
    => Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ApiErrorResponse Error(string code, string message) => new()
    {
        Code    = code,
        Error   = message,
        TraceId = HttpContext.TraceIdentifier
    };
}
