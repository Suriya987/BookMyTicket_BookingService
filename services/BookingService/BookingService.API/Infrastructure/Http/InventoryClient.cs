using System.Net.Http.Json;

namespace BookingService.API.Infrastructure.Http;

public interface IInventoryClient
{
    Task<List<ShowSeatInfo>> GetShowSeatsAsync(List<Guid> showSeatIds, CancellationToken ct = default);
}

public class ShowSeatInfo
{
    public Guid   ShowSeatId { get; set; }
    public Guid   ShowId     { get; set; }
    public Guid   SeatId     { get; set; }
    public string Status     { get; set; } = default!;
    public string Version    { get; set; } = default!;  // RowVersion as hex string
}

public class InventoryClient : IInventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<List<ShowSeatInfo>> GetShowSeatsAsync(
        List<Guid> showSeatIds, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "/v1/showseats/batch",
                new { ShowSeatIds = showSeatIds }, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<List<ShowSeatInfo>>(ct);

            return result ?? new List<ShowSeatInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inventory service call failed");
            throw;
        }
    }
}
