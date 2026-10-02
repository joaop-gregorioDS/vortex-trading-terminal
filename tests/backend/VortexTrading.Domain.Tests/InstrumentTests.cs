using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class InstrumentTests
{
    private static readonly InstrumentId Id = InstrumentId.From(new Guid("0d319028-b047-4e71-bd7d-9f6983dd17a4"));

    public static TheoryData<decimal, decimal, bool> PriceCases => new()
    {
        { 0.05m, 0m, true },
        { 0.05m, 10.00m, true },
        { 0.05m, 10.05m, true },
        { 0.05m, 10.10m, true },
        { 0.05m, 10.07m, false },
        { 0.00000001m, 1.23456789m, true },
        { 0.00000001m, 1.234567891m, false },
        { 0.0500m, 10.050000m, true },
        { 0.0000000000000000000000000001m, decimal.MaxValue, true },
        { 100m, decimal.MaxValue, false },
        { 0.0000000000000000000000000003m, 0.0000000000000000000000000007m, false }
    };

    public static TheoryData<decimal, decimal, bool> QuantityCases => new()
    {
        { 100m, 0m, true },
        { 100m, 100m, true },
        { 100m, 200m, true },
        { 100m, 150m, false },
        { 0.001m, 0m, true },
        { 0.001m, 0.001m, true },
        { 0.001m, 0.005m, true },
        { 0.001m, 0.0015m, false },
        { 0.0000000000000000000000000001m, decimal.MaxValue, true },
        { 0.0000000000000000000000000003m, 0.0000000000000000000000000006m, true },
        { 0.0000000000000000000000000003m, 0.0000000000000000000000000007m, false }
    };

    [Fact]
    public void ConstructionPreservesAllRequiredDomainComponents()
    {
        var symbol = new Symbol("PETR4");
        var venue = new Venue("B3");
        var currency = new Currency("BRL");
        var priceIncrement = new Price(0.0500m);
        var quantityIncrement = new Quantity(0.00100m);

        var instrument = new Instrument(Id, symbol, venue, AssetClass.Equity,
            currency, priceIncrement, quantityIncrement);

        Assert.Equal(Id, instrument.Id);
        Assert.Equal(symbol, instrument.Symbol);
        Assert.Equal(venue, instrument.Venue);
        Assert.Equal(AssetClass.Equity, instrument.AssetClass);
        Assert.Equal(currency, instrument.Currency);
        Assert.Same(priceIncrement, instrument.PriceIncrement);
        Assert.Same(quantityIncrement, instrument.QuantityIncrement);
        Assert.Equal(decimal.GetBits(0.0500m), decimal.GetBits(instrument.PriceIncrement.Value));
        Assert.Equal(decimal.GetBits(0.00100m), decimal.GetBits(instrument.QuantityIncrement.Value));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Symbol")]
    [InlineData("Venue")]
    [InlineData("Currency")]
    [InlineData("PriceIncrement")]
    [InlineData("QuantityIncrement")]
    public void RejectsMissingRequiredComponent(string component)
    {
        Assert.Throws<DomainException>(() => new Instrument(
            component == "Id" ? null : Id,
            component == "Symbol" ? null : new Symbol("PETR4"),
            component == "Venue" ? null : new Venue("B3"),
            AssetClass.Equity,
            component == "Currency" ? null : new Currency("BRL"),
            component == "PriceIncrement" ? null : new Price(0.01m),
            component == "QuantityIncrement" ? null : new Quantity(1m)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsZeroIncrementWithoutChangingGlobalPrimitives(bool zeroPriceIncrement)
    {
        var priceIncrement = new Price(zeroPriceIncrement ? 0m : 0.01m);
        var quantityIncrement = new Quantity(zeroPriceIncrement ? 1m : 0m);

        Assert.Throws<DomainException>(() => new Instrument(Id, new Symbol("PETR4"), new Venue("B3"),
            AssetClass.Equity, new Currency("BRL"), priceIncrement, quantityIncrement));
    }

    [Theory]
    [MemberData(nameof(PriceCases))]
    public void PriceAlignmentUsesExactDecimalRemainderWithoutChangingPrice(
        decimal increment, decimal value, bool expected)
    {
        var instrument = CreateInstrument(priceIncrement: increment);
        var price = new Price(value);
        var originalBits = decimal.GetBits(value);

        Assert.Equal(expected, instrument.IsValidPrice(price));
        Assert.Equal(originalBits, decimal.GetBits(price.Value));
        Assert.Equal(increment, instrument.PriceIncrement.Value);
    }

    [Theory]
    [MemberData(nameof(QuantityCases))]
    public void QuantityAlignmentSupportsIntegerAndFractionalStepsWithoutChangingQuantity(
        decimal increment, decimal value, bool expected)
    {
        var instrument = CreateInstrument(quantityIncrement: increment);
        var quantity = new Quantity(value);
        var originalBits = decimal.GetBits(value);

        Assert.Equal(expected, instrument.IsValidQuantity(quantity));
        Assert.Equal(originalBits, decimal.GetBits(quantity.Value));
        Assert.Equal(increment, instrument.QuantityIncrement.Value);
    }

    [Fact]
    public void AlignmentRejectsMissingValues()
    {
        var instrument = CreateInstrument();

        Assert.Throws<DomainException>(() => instrument.IsValidPrice(null));
        Assert.Throws<DomainException>(() => instrument.IsValidQuantity(null));
    }

    [Fact]
    public void SameIdentityAndEquivalentPropertiesAreEqual()
    {
        var first = CreateInstrument();
        var second = CreateInstrument();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void SameIdentityRemainsEqualWhenMarketPropertiesDiffer()
    {
        var first = CreateInstrument();
        var second = new Instrument(InstrumentId.From(Id.Value), new Symbol("OTHER"), new Venue("CUSTOM"),
            AssetClass.Crypto, new Currency("USDT"), new Price(0.0001m), new Quantity(0.001m));

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<Instrument> { first, second });
    }

    [Fact]
    public void DifferentIdentitiesRemainDistinctWithIdenticalMarketProperties()
    {
        var first = CreateInstrument();
        var second = new Instrument(
            InstrumentId.From(new Guid("12067718-caa9-4b98-996d-b73234605d9f")),
            first.Symbol, first.Venue, first.AssetClass, first.Currency,
            first.PriceIncrement, first.QuantityIncrement);

        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
        Assert.Equal(2, new HashSet<Instrument> { first, second }.Count);
    }

    [Fact]
    public void SameSymbolCanBelongToDifferentVenuesWithoutIdentityCollision()
    {
        var first = CreateInstrument();
        var second = new Instrument(
            InstrumentId.From(new Guid("12067718-caa9-4b98-996d-b73234605d9f")),
            first.Symbol, new Venue("CUSTOM"), first.AssetClass, first.Currency,
            first.PriceIncrement, first.QuantityIncrement);

        Assert.Equal(first.Symbol, second.Symbol);
        Assert.NotEqual(first.Venue, second.Venue);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EqualityHandlesNullAndOtherTypes()
    {
        var instrument = CreateInstrument();
        Instrument? missing = null;

        Assert.False(instrument.Equals((Instrument?)null));
        Assert.False(instrument.Equals((object)Id));
        Assert.False(instrument == missing);
        Assert.False(missing == instrument);
        Assert.True(missing == null);
        Assert.True(instrument != missing);
    }

    private static Instrument CreateInstrument(decimal priceIncrement = 0.05m, decimal quantityIncrement = 100m)
        => new(InstrumentId.From(Id.Value), new Symbol("PETR4"), new Venue("B3"), AssetClass.Equity,
            new Currency("BRL"), new Price(priceIncrement), new Quantity(quantityIncrement));
}
