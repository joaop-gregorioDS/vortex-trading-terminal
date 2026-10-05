using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Persistence.Configurations;

/// <summary>Maps one execution fill belonging to an Order.</summary>
/// <remarks>
/// ExecutionId is the primary key, so the database enforces the same uniqueness the domain uses to
/// deduplicate fills and reject conflicting content for a known execution. OrderQuantity is stored
/// alongside the fill because it is the original intent component the domain uses to resolve an
/// economic conflict; it is not a second authority over conservation.
/// </remarks>
internal sealed class OrderFillConfiguration : IEntityTypeConfiguration<OrderFill>
{
    public void Configure(EntityTypeBuilder<OrderFill> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_fills");
        builder.HasKey(fill => fill.ExecutionId);

        builder.Property(fill => fill.ExecutionId)
            .HasConversion(PersistenceConverters.ExecutionId)
            .ValueGeneratedNever();

        builder.Property(fill => fill.OrderId)
            .HasConversion(PersistenceConverters.OrderId)
            .IsRequired();

        builder.Property(fill => fill.Quantity)
            .HasConversion(PersistenceConverters.Quantity)
            .HasColumnType(PersistenceConverters.ExactNumeric)
            .IsRequired();

        builder.Property(fill => fill.Price)
            .HasConversion(PersistenceConverters.Price)
            .HasColumnType(PersistenceConverters.ExactNumeric)
            .IsRequired();

        builder.Property(fill => fill.ExecutedAt)
            .HasConversion(PersistenceConverters.UtcInstant)
            .IsRequired();

        // The aggregate root owns the collection; the shadow foreign key is configured by
        // OrderConfiguration. Reading fills by order is the only query pattern defined today.
        builder.HasIndex("OrderId");
    }
}
