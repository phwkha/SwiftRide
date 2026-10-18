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
        builder.HasIndex(t => t.TripId);

        builder.Property(t => t.TransactionType)
            .HasColumnName("transaction_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 2)
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
