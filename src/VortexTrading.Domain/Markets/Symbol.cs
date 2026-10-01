using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Markets;

public sealed record Symbol
{
    public string Value { get; }

    public Symbol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Symbol must not be null, empty or whitespace.");
        }

        Value = value.Trim().ToUpperInvariant();
    }

    public override string ToString() => Value;
}
