using Stripe;

namespace BookingService.API.Infrastructure.Http;

public interface IPaymentClient
{
    Task<PaymentResult> ProcessPaymentAsync(
        Guid bookingId, Guid userId,
        decimal amount, string cardNumber,
        string cvv, int expiryMonth, int expiryYear,
        CancellationToken ct = default);

    Task<RefundResult> RefundPaymentAsync(
        string providerTransactionId,
        decimal amount,
        CancellationToken ct = default);
}

public class PaymentResult
{
    public bool    Success               { get; set; }
    public string? ProviderTransactionId { get; set; }  // Stripe charge ID
    public string? FailureReason         { get; set; }
}

public class RefundResult
{
    public bool    Success       { get; set; }
    public string? FailureReason { get; set; }
}

public class StripePaymentClient : IPaymentClient
{
    private readonly ILogger<StripePaymentClient> _logger;

    public StripePaymentClient(IConfiguration config, ILogger<StripePaymentClient> logger)
    {
        // Set Stripe secret key from config
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        _logger = logger;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid bookingId, Guid userId,
        decimal amount, string cardNumber,
        string cvv, int expiryMonth, int expiryYear,
        CancellationToken ct = default)
    {
        try
        {
            //_logger.LogInformation(
            //    "Processing Stripe payment for booking {BookingId} amount={Amount}",
            //    bookingId, amount);

            //// Step 1: Create payment method from card details
            //var paymentMethodService = new PaymentMethodService();
            //var paymentMethod = await paymentMethodService.CreateAsync(
            //    new PaymentMethodCreateOptions
            //    {
            //        Type = "card",
            //        Card = new PaymentMethodCardOptions
            //        {
            //            Number    = cardNumber,
            //            ExpMonth  = expiryMonth,
            //            ExpYear   = expiryYear,
            //            Cvc       = cvv
            //        }
            //    }, cancellationToken: ct);

            //// Step 2: Create and confirm payment intent
            //var paymentIntentService = new PaymentIntentService();
            //var paymentIntent = await paymentIntentService.CreateAsync(
            //    new PaymentIntentCreateOptions
            //    {
            //        Amount             = (long)(amount * 100),  // Stripe uses paise/cents
            //        Currency           = "inr",
            //        PaymentMethod      = paymentMethod.Id,
            //        Confirm            = true,
            //        Metadata           = new Dictionary<string, string>
            //        {
            //            { "BookingId", bookingId.ToString() },
            //            { "UserId",    userId.ToString()    }
            //        },
            //        AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            //        {
            //            Enabled         = true,
            //            AllowRedirects  = "never"
            //        }
            //    }, cancellationToken: ct);

            //if (paymentIntent.Status == "succeeded")
            //{
            //    _logger.LogInformation(
            //        "Stripe payment succeeded for booking {BookingId} chargeId={ChargeId}",
            //        bookingId, paymentIntent.Id);

            //    return new PaymentResult
            //    {
            //        Success               = true,
            //        ProviderTransactionId = paymentIntent.Id
            //    };
            //}

            //_logger.LogWarning(
            //    "Stripe payment not succeeded for booking {BookingId} status={Status}",
            //    bookingId, paymentIntent.Status);

            //return new PaymentResult
            //{
            //    Success       = false,
            //    FailureReason = $"Payment status: {paymentIntent.Status}"
            //};
            await Task.Delay(300, ct);  // simulate processing

            _logger.LogInformation(
                "Mock payment processed for booking {BookingId} amount={Amount}",
                bookingId, amount);

            return new PaymentResult
            {
                Success = true,
                ProviderTransactionId = $"mock_txn_{Guid.NewGuid():N}"
            };

        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "Stripe exception for booking {BookingId}: {Message}",
                bookingId, ex.Message);

            return new PaymentResult
            {
                Success       = false,
                FailureReason = ex.StripeError?.Message ?? ex.Message
            };
        }
    }

    public async Task<RefundResult> RefundPaymentAsync(
        string providerTransactionId,
        decimal amount,
        CancellationToken ct = default)
    {
        try
        {
            var refundService = new RefundService();
            var refund = await refundService.CreateAsync(
                new RefundCreateOptions
                {
                    PaymentIntent = providerTransactionId,
                    Amount        = (long)(amount * 100)
                }, cancellationToken: ct);

            return new RefundResult { Success = refund.Status == "succeeded" };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed: {Message}", ex.Message);
            return new RefundResult
            {
                Success       = false,
                FailureReason = ex.Message
            };
        }
    }
}
