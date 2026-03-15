using BookingService.API.Models.DTOs;
using BookingService.API.Services;
using System.Text.Json;

namespace BookingService.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex);
        }
    }

    private async Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, code) = ex switch
        {
            SeatNotAvailableException        => (409, "SEAT_NOT_AVAILABLE"),
            SeatCurrentlyLockedException     => (409, "SEAT_LOCKED"),
            PaymentFailedException           => (402, "PAYMENT_FAILED"),
            BookingNotFoundException         => (404, "BOOKING_NOT_FOUND"),
            UnauthorizedBookingException     => (403, "UNAUTHORIZED"),
            BookingAlreadyCancelledException => (400, "ALREADY_CANCELLED"),
            InvalidBookingStateException     => (400, "INVALID_STATE"),
            _                               => (500, "INTERNAL_ERROR")
        };

        if (status == 500)
            _logger.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
        else
            _logger.LogWarning("Handled {Code}: {Message}", code, ex.Message);

        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse
        {
            Code    = code,
            Error   = status == 500 ? "An unexpected error occurred" : ex.Message,
            TraceId = ctx.TraceIdentifier
        }));
    }
}
