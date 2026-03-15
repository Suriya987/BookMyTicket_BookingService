using Confluent.Kafka;
using System.Text.Json;

namespace BookingService.API.Infrastructure.Kafka;

public interface IKafkaPublisherService
{
    Task PublishBookingConfirmedAsync(
        Guid bookingId, Guid userId, Guid showId,
        List<Guid> showSeatIds, decimal totalAmount,
        CancellationToken ct = default);

    Task PublishBookingFailedAsync(
        Guid bookingId, Guid userId,
        List<Guid> showSeatIds, string reason,
        CancellationToken ct = default);

    Task PublishBookingCancelledAsync(
        Guid bookingId, Guid userId,
        List<Guid> showSeatIds, string reason,
        CancellationToken ct = default);
}

public class KafkaPublisherService : IKafkaPublisherService, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaPublisherService> _logger;

    private const string ConfirmedTopic  = "booking.confirmed";
    private const string FailedTopic     = "booking.failed";
    private const string CancelledTopic  = "booking.cancelled";

    public KafkaPublisherService(
        IProducer<string, string> producer,
        ILogger<KafkaPublisherService> logger)
    {
        _producer = producer;
        _logger   = logger;
    }

    public Task PublishBookingConfirmedAsync(
        Guid bookingId, Guid userId, Guid showId,
        List<Guid> showSeatIds, decimal totalAmount,
        CancellationToken ct = default)
        => PublishAsync(ConfirmedTopic, bookingId.ToString(), new
        {
            BookingId   = bookingId,
            UserId      = userId,
            ShowId      = showId,
            ShowSeatIds = showSeatIds,
            TotalAmount = totalAmount,
            OccurredAt  = DateTimeOffset.UtcNow
        }, ct);

    public Task PublishBookingFailedAsync(
        Guid bookingId, Guid userId,
        List<Guid> showSeatIds, string reason,
        CancellationToken ct = default)
        => PublishAsync(FailedTopic, bookingId.ToString(), new
        {
            BookingId   = bookingId,
            UserId      = userId,
            ShowSeatIds = showSeatIds,
            Reason      = reason,
            OccurredAt  = DateTimeOffset.UtcNow
        }, ct);

    public Task PublishBookingCancelledAsync(
        Guid bookingId, Guid userId,
        List<Guid> showSeatIds, string reason,
        CancellationToken ct = default)
        => PublishAsync(CancelledTopic, bookingId.ToString(), new
        {
            BookingId   = bookingId,
            UserId      = userId,
            ShowSeatIds = showSeatIds,
            Reason      = reason,
            OccurredAt  = DateTimeOffset.UtcNow
        }, ct);

    private async Task PublishAsync<T>(
        string topic, string key, T payload, CancellationToken ct)
    {
        try
        {
            //using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            //var result = await _producer.ProduceAsync(topic,
            //    new Message<string, string>
            //    {
            //        Key   = key,
            //        Value = JsonSerializer.Serialize(payload)
            //    }, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var result = await _producer.ProduceAsync(topic,
                new Message<string, string>
                {
                    Key = key,
                    Value = JsonSerializer.Serialize(payload)
                }, cts.Token);

            _logger.LogInformation(
                "Published {Topic} partition={P} offset={O}",
                topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Kafka publish failed topic={Topic}", topic);
            throw;
        }
    }

    public void Dispose() => _producer?.Dispose();
}
