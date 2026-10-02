using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class TradeTests
{
    private static readonly InstrumentId Id = InstrumentId.From(new Guid("48a2b1d6-f95e-4a40-a182-0b6ef410ca22"));
    private static readonly UtcInstant EventTime = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567));
    private static readonly UtcInstant ReceivedTime = new(EventTime.Value.AddMilliseconds(45));

    public static TheoryData<decimal> PositivePrices => new()
    {
        0.0000000000000000000000000001m,
        7.1234567890123456789012345678m,
        decimal.MaxValue
    };

    public static TheoryData<decimal> PositiveQuantities => new()
    {
        1.0000m,
        0.00100m,
        0.0000000000000000000000000001m,
        7.1234567890123456789012345678m,
        decimal.MaxValue
    };

    [Fact]
    public void ConstructionPreservesAllComponentsWithoutChangingPrecision()
    {
        var price = new Price(10.0500m);
        var quantity = new Quantity(0.00100m);

        var trade = new Trade(Id, EventTime, ReceivedTime, price, quantity);

        Assert.Same(Id, trade.InstrumentId);
        Assert.Same(EventTime, trade.EventTime);
        Assert.Same(ReceivedTime, trade.ReceivedTime);
        Assert.Same(price, trade.Price);
        Assert.Same(quantity, trade.Quantity);
        Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(trade.Price.Value));
        Assert.Equal(decimal.GetBits(0.00100m), decimal.GetBits(trade.Quantity.Value));
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("EventTime")]
    [InlineData("ReceivedTime")]
    [InlineData("Price")]
    [InlineData("Quantity")]
    public void RejectsEachMissingRequiredComponent(string component)
    {
        Assert.Throws<DomainException>(() => new Trade(
            component == "InstrumentId" ? null : Id,
            component == "EventTime" ? null : EventTime,
            component == "ReceivedTime" ? null : ReceivedTime,
            component == "Price" ? null : new Price(10m),
            component == "Quantity" ? null : new Quantity(1m)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsZeroPriceOrQuantityWithoutChangingGlobalPrimitives(bool zeroPrice)
    {
        var price = new Price(zeroPrice ? 0m : 10m);
        var quantity = new Quantity(zeroPrice ? 1m : 0m);

        Assert.Throws<DomainException>(() => new Trade(Id, EventTime, ReceivedTime, price, quantity));
    }

    [Theory]
    [MemberData(nameof(PositivePrices))]
    public void AcceptsPositivePricesIncludingHighPrecisionAndExtremeValues(decimal value)
    {
        var trade = CreateTrade(price: value);

        Assert.Equal(decimal.GetBits(value), decimal.GetBits(trade.Price.Value));
    }

    [Theory]
    [MemberData(nameof(PositiveQuantities))]
    public void AcceptsPositiveFractionalAndExtremeQuantitiesWithoutQuantization(decimal value)
    {
        var trade = CreateTrade(quantity: value);

        Assert.Equal(decimal.GetBits(value), decimal.GetBits(trade.Quantity.Value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void PreservesBothTimesWithoutAssumingReceivedTimeOrdering(int receivedDeltaTicks)
    {
        var received = new UtcInstant(EventTime.Value.AddTicks(receivedDeltaTicks).ToOffset(TimeSpan.FromHours(-3)));

        var trade = new Trade(Id, EventTime, received, new Price(10m), new Quantity(1m));

        Assert.Same(EventTime, trade.EventTime);
        Assert.Same(received, trade.ReceivedTime);
        Assert.Equal(EventTime.Value.UtcTicks, trade.EventTime.Value.UtcTicks);
        Assert.Equal(EventTime.Value.UtcTicks + receivedDeltaTicks, trade.ReceivedTime.Value.UtcTicks);
        Assert.Equal(TimeSpan.Zero, trade.EventTime.Value.Offset);
        Assert.Equal(TimeSpan.Zero, trade.ReceivedTime.Value.Offset);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreservesRepresentableTemporalExtremesWithoutArithmetic(bool earliestEvent)
    {
        var eventTime = new UtcInstant(earliestEvent ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue);
        var receivedTime = new UtcInstant(earliestEvent ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue);

        var trade = new Trade(Id, eventTime, receivedTime, new Price(10m), new Quantity(1m));

        Assert.Same(eventTime, trade.EventTime);
        Assert.Same(receivedTime, trade.ReceivedTime);
    }

    [Fact]
    public void StructuralEqualityAndHashingUseEquivalentComponentValues()
    {
        var first = CreateTrade();
        var second = new Trade(InstrumentId.From(Id.Value),
            new UtcInstant(EventTime.Value.ToOffset(TimeSpan.FromHours(-3))),
            new UtcInstant(ReceivedTime.Value.ToOffset(TimeSpan.FromHours(3))),
            new Price(10.00m), new Quantity(1.00m));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<Trade> { first, second });
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("EventTime")]
    [InlineData("ReceivedTime")]
    [InlineData("Price")]
    [InlineData("Quantity")]
    public void DifferentObservationComponentsMakeTradesUnequal(string component)
    {
        var first = CreateTrade();
        var second = new Trade(
            component == "InstrumentId" ? InstrumentId.From(new Guid("70d3caa7-40d5-4fc0-bc0e-e89cfc38bea2")) : Id,
            component == "EventTime" ? new UtcInstant(EventTime.Value.AddTicks(1)) : EventTime,
            component == "ReceivedTime" ? new UtcInstant(ReceivedTime.Value.AddTicks(1)) : ReceivedTime,
            new Price(component == "Price" ? 10.5m : 10m),
            new Quantity(component == "Quantity" ? 2m : 1m));

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void ValuesEqualityAndInvariantsDoNotDependOnCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var expected = CreateTrade(price: 10.0500m, quantity: 0.00100m);

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var actual = CreateTrade(price: 10.0500m, quantity: 0.00100m);

            Assert.Equal(expected, actual);
            Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
            Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(actual.Price.Value));
            Assert.Equal(decimal.GetBits(0.00100m), decimal.GetBits(actual.Quantity.Value));
            Assert.Equal("10.0500", actual.Price.ToString());
            Assert.Equal("0.00100", actual.Quantity.ToString());
            Assert.Throws<DomainException>(() => CreateTrade(price: 0m));
            Assert.Throws<DomainException>(() => CreateTrade(quantity: 0m));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void TradeConstructionDoesNotValidateInstrumentIncrements()
    {
        var instrument = new Instrument(Id, new Symbol("TEST"), new Venue("CUSTOM"), AssetClass.Equity,
            new Currency("USD"), new Price(0.05m), new Quantity(100m));
        var trade = CreateTrade(price: 10.07m, quantity: 0.001m);

        Assert.Equal(instrument.Id, trade.InstrumentId);
        Assert.False(instrument.IsValidPrice(trade.Price));
        Assert.False(instrument.IsValidQuantity(trade.Quantity));
    }

    private static Trade CreateTrade(decimal price = 10m, decimal quantity = 1m)
        => new(InstrumentId.From(Id.Value), EventTime, ReceivedTime, new Price(price), new Quantity(quantity));
}
