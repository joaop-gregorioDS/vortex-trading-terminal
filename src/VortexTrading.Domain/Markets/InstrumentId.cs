using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Markets;

public sealed record InstrumentId
{
    public Guid Value { get; }

    public InstrumentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Instrument ID must not be empty.");
        }

        Value = value;
    }

    public static InstrumentId New() => new(Guid.NewGuid());

    public static InstrumentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}
