using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BookingService.API.Models;

[Table("PaymentProcessing")]
[Index("BookingId", Name = "IX_PaymentProcessing_BookingId")]
[Index("ProcessedAt", Name = "IX_PaymentProcessing_ProcessedAt")]
[Index("UserId", Name = "IX_PaymentProcessing_UserId")]
public partial class PaymentProcessing
{
    [Key]
    public Guid ProcessingId { get; set; }

    public Guid UserId { get; set; }

    public Guid BookingId { get; set; }

    public Guid? CardInfoId { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Amount { get; set; }

    [StringLength(20)]
    public string Status { get; set; } = null!;

    [StringLength(500)]
    public string? FailureReason { get; set; }

    [StringLength(100)]
    public string? PaymentProvider { get; set; }

    [StringLength(200)]
    public string? ProviderTransactionId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }

    [ForeignKey("BookingId")]
    [InverseProperty("PaymentProcessings")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("CardInfoId")]
    [InverseProperty("PaymentProcessings")]
    public virtual CardInfo? CardInfo { get; set; }
}
