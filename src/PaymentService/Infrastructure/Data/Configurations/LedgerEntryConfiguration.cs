using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Data.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.TransactionId).HasColumnName("transaction_id").IsRequired();
        builder.HasIndex(e => e.TransactionId);

        builder.Property(e => e.AccountId).HasColumnName("account_id").IsRequired();
        builder.HasIndex(e => e.AccountId);

        builder.Property(e => e.EntryType)
            .HasColumnName("entry_type")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
