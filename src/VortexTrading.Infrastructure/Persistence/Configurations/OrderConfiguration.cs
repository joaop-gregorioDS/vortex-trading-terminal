using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Persistence.Configurations;

/// <summary>Maps the Order aggregate, which owns its fills as part of the same consistency boundary.</summary>
/// <remarks>
/// Order has a private constructor and only creation factories, so EF Core materializes it through
/// its backing fields. ADR-035 records why no rehydration factory was added to the domain: the
/// mapping is a technical concern and a public factory would let callers build Orders with arbitrary
/// lifecycle state and timestamps, bypassing the creation guards.
/// </remarks>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("orders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasConversion(PersistenceConverters.OrderId)
            .ValueGeneratedNever();

        builder.Property(order => order.InstrumentId)
            .HasConversion(PersistenceConverters.InstrumentId)
            .IsRequired();

        // Enum members are stored by name: their numeric values are a compiler detail, so an
        // integer column would corrupt history if members were ever reordered.
        builder.Property(order => order.Side).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(order => order.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(order => order.TimeInForce).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(order => order.RejectionReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(order => order.Quantity)
            .HasConversion(PersistenceConverters.Quantity)
            .HasColumnType(PersistenceConverters.ExactNumeric)
            .IsRequired();

        builder.Property(order => order.LimitPrice)
            .HasConversion(PersistenceConverters.NullablePrice)
            .HasColumnType(PersistenceConverters.ExactNumeric);

        builder.Property(order => order.CreatedAt)
            .HasConversion(PersistenceConverters.UtcInstant)
            .IsRequired();

        builder.Property(order => order.LastUpdatedAt)
            .HasConversion(PersistenceConverters.UtcInstant)
            .IsRequired();

        // FilledQuantity, RemainingQuantity and CompletedAt are derived from the fills and the
        // lifecycle, so they are never stored. Storing them would create a second authority over
        // the quantity conservation the domain already proves.
        builder.Ignore(order => order.FilledQuantity);
        builder.Ignore(order => order.RemainingQuantity);
        builder.Ignore(order => order.CompletedAt);

        builder.HasMany(order => order.Fills)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        // Fills is exposed as IReadOnlyList and backed by _fills; never let EF replace the instance.
        builder.Navigation(order => order.Fills)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_fills")
            .AutoInclude(false);

        // One index per documented access path. The aggregate is read by instrument for future
        // per-instrument queries and by status to find orders still active in an execution scope.
        builder.HasIndex(order => order.InstrumentId);
        builder.HasIndex(order => order.Status);
    }
}
