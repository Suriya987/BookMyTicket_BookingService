using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BookingService.API.Models;

[Index("ShowId", Name = "IX_Bookings_ShowId")]
[Index("UserId", Name = "IX_Bookings_UserId")]
[Index("IdempotencyKey", Name = "UX_Bookings_IdempotencyKey", IsUnique = true)]
public partial class Booking
{
    [Key]
    public Guid BookingId { get; set; }

    public Guid UserId { get; set; }

    public Guid ShowId { get; set; }

    public string ShowSeatIds { get; set; } = null!;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TotalAmount { get; set; }

    [StringLength(20)]
    public string Status { get; set; } = null!;

    [StringLength(500)]
    public string? FailureReason { get; set; }

    [StringLength(100)]
    public string IdempotencyKey { get; set; } = null!;

    public DateTimeOffset HeldUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [InverseProperty("Booking")]
    public virtual ICollection<PaymentProcessing> PaymentProcessings { get; set; } = new List<PaymentProcessing>();
}
