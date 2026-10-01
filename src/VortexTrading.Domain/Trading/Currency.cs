using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record Currency
{
    public string Value { get; }

    public Currency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Currency must not be null, empty or whitespace.");
        }

        Value = value.Trim().ToUpperInvariant();
    }

    public override string ToString() => Value;
}
