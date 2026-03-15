using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BookingService.API.Models;

[Table("CardInfo")]
[Index("UserId", Name = "IX_CardInfo_UserId")]
public partial class CardInfo
{
    [Key]
    public Guid CardInfoId { get; set; }

    public Guid UserId { get; set; }

    [StringLength(200)]
    public string CardHolderName { get; set; } = null!;

    [StringLength(20)]
    public string CardNumber { get; set; } = null!;

    [Column("CVV")]
    [StringLength(5)]
    public string Cvv { get; set; } = null!;

    [StringLength(50)]
    public string CardType { get; set; } = null!;

    public int ExpiryMonth { get; set; }

    public int ExpiryYear { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [InverseProperty("CardInfo")]
    public virtual ICollection<PaymentProcessing> PaymentProcessings { get; set; } = new List<PaymentProcessing>();
}
