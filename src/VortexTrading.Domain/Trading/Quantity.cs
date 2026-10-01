using System.Globalization;
using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record Quantity
{
    public decimal Value { get; }

    public Quantity(decimal value)
    {
        if (value < 0)
        {
            throw new DomainException("Quantity must not be negative.");
        }

        Value = value;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
