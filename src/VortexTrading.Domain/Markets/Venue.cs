using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Markets;

public sealed record Venue
{
    public string Value { get; }

    public Venue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Venue must not be null, empty or whitespace.");
        }

        Value = value.Trim().ToUpperInvariant();
    }

    public override string ToString() => Value;
}
