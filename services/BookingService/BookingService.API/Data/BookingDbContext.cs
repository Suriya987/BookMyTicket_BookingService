using System;
using System.Collections.Generic;
using BookingService.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingService.API.Data;

public partial class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<CardInfo> CardInfos { get; set; }

    public virtual DbSet<PaymentProcessing> PaymentProcessings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasIndex(e => new { e.Status, e.HeldUntil }, "IX_Bookings_Status_HeldUntil").HasFilter("([Status]='PENDING')");

            entity.Property(e => e.BookingId).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("PENDING");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
        });

        modelBuilder.Entity<CardInfo>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.CardNumber }, "UX_CardInfo_UserId_CardNumber")
                .IsUnique()
                .HasFilter("([IsActive]=(1))");

            entity.Property(e => e.CardInfoId).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
        });

        modelBuilder.Entity<PaymentProcessing>(entity =>
        {
            entity.Property(e => e.ProcessingId).HasDefaultValueSql("(newsequentialid())");
            entity.Property(e => e.ProcessedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.Booking).WithMany(p => p.PaymentProcessings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PaymentProcessing_Bookings");

            entity.HasOne(d => d.CardInfo).WithMany(p => p.PaymentProcessings).HasConstraintName("FK_PaymentProcessing_CardInfo");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
