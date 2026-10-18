using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripService.Domain.Entities;

namespace TripService.Infrastructure.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.EventType).HasMaxLength(255).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.Property(m => m.ProcessedAt).IsRequired(false);
        builder.HasIndex(m => m.ProcessedAt);

        builder.Property(m => m.Error);

        builder.Ignore(m => m.Type);
        builder.Ignore(m => m.OccurredOn);
        builder.Ignore(m => m.ProcessedOn);
    }
}
