using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripService.Domain.Entities;

namespace TripService.Infrastructure.Data.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.RiderId).IsRequired();
        builder.HasIndex(t => t.RiderId);

        builder.Property(t => t.DriverId).IsRequired(false);
        builder.HasIndex(t => t.DriverId);

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(t => t.Status);

        builder.Property(t => t.PickupLatitude).HasPrecision(9, 6).IsRequired();
        builder.Property(t => t.PickupLongitude).HasPrecision(9, 6).IsRequired();
        builder.Property(t => t.PickupAddress).HasMaxLength(255).IsRequired();

        builder.Property(t => t.DropoffLatitude).HasPrecision(9, 6).IsRequired();
        builder.Property(t => t.DropoffLongitude).HasPrecision(9, 6).IsRequired();
        builder.Property(t => t.DropoffAddress).HasMaxLength(255).IsRequired();

        builder.Property(t => t.DistanceKm).HasPrecision(8, 2).IsRequired();
        builder.Property(t => t.EstimatedFare).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.ActualFare).HasPrecision(18, 2).IsRequired(false);
        builder.Property(t => t.FareConfirmed).HasDefaultValue(false).IsRequired();

        builder.Property(t => t.DriverRejectionCount).HasDefaultValue(0);
        builder.Property(t => t.CancellationReason).HasMaxLength(255);

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        builder.HasMany(t => t.StatusHistories)
            .WithOne(h => h.Trip)
            .HasForeignKey(h => h.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
