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

        builder.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(255).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at").IsRequired(false);
        builder.HasIndex(m => m.ProcessedAt);

        builder.Property(m => m.Error).HasColumnName("error");

        builder.Ignore(m => m.Type);
        builder.Ignore(m => m.OccurredOn);
        builder.Ignore(m => m.ProcessedOn);
    }
}
