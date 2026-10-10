using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Data.Configurations;

public class PaymentTokenConfiguration : IEntityTypeConfiguration<PaymentToken>
{
    public void Configure(EntityTypeBuilder<PaymentToken> builder)
    {
        builder.ToTable("payment_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(t => t.UserId);

        builder.Property(t => t.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Token).HasColumnName("token").HasMaxLength(255).IsRequired();
        builder.Property(t => t.IsDefault).HasColumnName("is_default").HasDefaultValue(false).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
