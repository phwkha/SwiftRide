using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.HasIndex(a => a.OwnerId);

        builder.Property(a => a.OwnerType).HasColumnName("owner_type").HasMaxLength(50).IsRequired();

        builder.Property(a => a.AccountType)
            .HasColumnName("account_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND").IsRequired();

        builder.Property(a => a.Balance)
            .HasColumnName("balance")
            .HasPrecision(18, 2)
            .HasDefaultValue(0.00m)
            .IsRequired();

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(a => a.LedgerEntries)
            .WithOne(e => e.Account)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
