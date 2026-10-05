using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record OrderId
{
    public Guid Value { get; }

    public OrderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Order ID must not be empty.");
        }

        Value = value;
    }

    public static OrderId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}
