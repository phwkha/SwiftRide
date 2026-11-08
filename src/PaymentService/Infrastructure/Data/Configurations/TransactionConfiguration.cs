using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.TripId).HasColumnName("trip_id").IsRequired(false);

        // Chong thu tien 2 lan cho cung 1 chuyen di o tang vat ly PostgreSQL (SD 12, SD 13)
        builder.HasIndex(t => t.TripId)
            .HasDatabaseName("ux_transactions_trip_success")
            .IsUnique()
            .HasFilter("\"status\" = 'Success' AND \"transaction_type\" = 'RidePayment'");

        builder.Property(t => t.TransactionType)
            .HasColumnName("transaction_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.PaymentMethod)
            .HasColumnName("payment_method")
            .HasMaxLength(50)
            .HasDefaultValue("Wallet")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(t => t.ErrorDetail)
            .HasColumnName("error_detail");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasMany(t => t.LedgerEntries)
            .WithOne(e => e.Transaction)
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
