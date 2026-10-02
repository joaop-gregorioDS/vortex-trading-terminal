using System.Globalization;

namespace VortexTrading.Domain.Common.Time;

public sealed record UtcInstant : IComparable<UtcInstant>
{
    public DateTimeOffset Value { get; }

    public UtcInstant(DateTimeOffset value)
    {
        Value = value.ToUniversalTime();
    }

    public int CompareTo(UtcInstant? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("O", CultureInfo.InvariantCulture);
}
