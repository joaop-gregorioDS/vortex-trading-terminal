using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversions shared by the persistence model. Keeping them in one place prevents two entities
/// from storing the same domain concept differently.
/// </summary>
/// <remarks>
/// Exact financial values are stored as unbounded PostgreSQL <c>numeric</c> so that both the value
/// and the scale of the .NET <see cref="decimal"/> survive a round trip. ADR-002 and ADR-035 forbid
/// declaring a fixed precision/scale for them: that would silently round a value the domain treats
/// as exact. Enum values are stored by name because the numeric value of a C# enum is a compiler
/// detail and reordering members would corrupt historical rows.
/// </remarks>
internal static class PersistenceConverters
{
    /// <summary>PostgreSQL unbounded numeric; preserves the exact decimal value and scale.</summary>
    internal const string ExactNumeric = "numeric";

    internal static readonly ValueConverter<OrderId, Guid> OrderId =
        new(id => id.Value, value => new OrderId(value));

    internal static readonly ValueConverter<InstrumentId, Guid> InstrumentId =
        new(id => id.Value, value => new InstrumentId(value));

    internal static readonly ValueConverter<ExecutionId, Guid> ExecutionId =
        new(id => id.Value, value => new ExecutionId(value));

    internal static readonly ValueConverter<Currency, string> Currency =
        new(currency => currency.Value, value => new Currency(value));

    internal static readonly ValueConverter<Price, decimal> Price =
        new(price => price.Value, value => new Price(value));

    /// <summary>Market orders have no limit price, so the column is nullable in the model.</summary>
    internal static readonly ValueConverter<Price?, decimal?> NullablePrice =
        new(price => price == null ? null : price.Value, value => value == null ? null : new Price(value.Value));

    internal static readonly ValueConverter<Quantity, decimal> Quantity =
        new(quantity => quantity.Value, value => new Quantity(value));

    internal static readonly ValueConverter<UtcInstant, DateTimeOffset> UtcInstant =
        new(instant => TruncateToMicroseconds(instant.Value), value => new UtcInstant(value));

    /// <summary>
    /// PostgreSQL <c>timestamptz</c> resolves to microseconds, while a .NET tick is 100 nanoseconds.
    /// Truncating here makes the loss explicit and happens before the value reaches the database, so
    /// the round trip is idempotent: an instant already truncated is unchanged when stored again.
    /// Without this, a value such as 13:30:00.1234567 would come back as .1234560 and equality
    /// comparisons would silently fail after a reload.
    /// </summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        const long ticksPerMicrosecond = TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(value.Ticks - (value.Ticks % ticksPerMicrosecond), value.Offset);
    }
}
