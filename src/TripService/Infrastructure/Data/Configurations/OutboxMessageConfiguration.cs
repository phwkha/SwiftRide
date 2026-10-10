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

        builder.Property(m => m.OccurredOn).IsRequired();
        builder.Property(m => m.Type).HasMaxLength(255).IsRequired();
        builder.Property(m => m.Payload).IsRequired();

        builder.Property(m => m.ProcessedOn).IsRequired(false);
        builder.HasIndex(m => m.ProcessedOn);
    }
}
