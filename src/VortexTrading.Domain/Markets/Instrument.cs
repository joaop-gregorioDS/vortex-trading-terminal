using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Markets;

public sealed class Instrument : IEquatable<Instrument>
{
    public InstrumentId Id { get; }
    public Symbol Symbol { get; }
    public Venue Venue { get; }
    public AssetClass AssetClass { get; }
    public Currency Currency { get; }
    public Price PriceIncrement { get; }
    public Quantity QuantityIncrement { get; }

    public Instrument(
        InstrumentId? id,
        Symbol? symbol,
        Venue? venue,
        AssetClass assetClass,
        Currency? currency,
        Price? priceIncrement,
        Quantity? quantityIncrement)
    {
        if (id is null || symbol is null || venue is null || currency is null
            || priceIncrement is null || quantityIncrement is null)
        {
            throw new DomainException("Instrument requires an ID, symbol, venue, currency and increments.");
        }

        if (!Enum.IsDefined(assetClass))
        {
            throw new DomainException("Instrument requires a supported asset class.");
        }

        if (priceIncrement.Value <= 0)
        {
            throw new DomainException("Instrument price increment must be positive.");
        }

        if (quantityIncrement.Value <= 0)
        {
            throw new DomainException("Instrument quantity increment must be positive.");
        }

        Id = id;
        Symbol = symbol;
        Venue = venue;
        AssetClass = assetClass;
        Currency = currency;
        PriceIncrement = priceIncrement;
        QuantityIncrement = quantityIncrement;
    }

    public bool IsValidPrice(Price? price)
    {
        if (price is null)
        {
            throw new DomainException("Price alignment requires a price.");
        }

        return price.Value % PriceIncrement.Value == 0;
    }

    public bool IsValidQuantity(Quantity? quantity)
    {
        if (quantity is null)
        {
            throw new DomainException("Quantity alignment requires a quantity.");
        }

        return quantity.Value % QuantityIncrement.Value == 0;
    }

    public bool Equals(Instrument? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Instrument other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Instrument? left, Instrument? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(Instrument? left, Instrument? right) => !(left == right);
}
