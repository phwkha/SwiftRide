using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.OccurredOn).HasColumnName("occurred_on").IsRequired();
        builder.Property(m => m.Type).HasColumnName("type").HasMaxLength(255).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").IsRequired();

        builder.Property(m => m.ProcessedOn).HasColumnName("processed_on").IsRequired(false);
        builder.HasIndex(m => m.ProcessedOn);

        builder.Property(m => m.Error).HasColumnName("error");
    }
}
