using System.Globalization;
using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record Price
{
    public decimal Value { get; }

    public Price(decimal value)
    {
        if (value < 0)
        {
            throw new DomainException("Price must not be negative.");
        }

        Value = value;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
