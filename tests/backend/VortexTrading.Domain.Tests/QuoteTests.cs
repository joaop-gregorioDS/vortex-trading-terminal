using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class QuoteTests
{
    private static readonly InstrumentId Id = InstrumentId.From(new Guid("0d319028-b047-4e71-bd7d-9f6983dd17a4"));
    private static readonly UtcInstant EventTime = new(new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero).AddTicks(1234567));
    private static readonly UtcInstant ReceivedTime = new(EventTime.Value.AddMilliseconds(45));

    public static TheoryData<decimal, decimal, bool> SpreadCases => new()
    {
        { 10.00m, 10.05m, true },
        { 10.00m, 10.00m, true },
        { 10.05m, 10.00m, false }
    };

    public static TheoryData<decimal, decimal> QuantityCases => new()
    {
        { 0m, 1m },
        { 1m, 0m },
        { 0m, 0m },
        { 0.125m, 0.00100m }
    };

    public static TheoryData<decimal> PreciseValues => new()
    {
        0.0000000000000000000000000001m,
        7.1234567890123456789012345678m,
        decimal.MaxValue
    };

    [Fact]
    public void ConstructionPreservesAllComponentsWithoutChangingPrecision()
    {
        var bidPrice = new Price(10.0500m);
        var askPrice = new Price(10.0700m);
        var bidQuantity = new Quantity(0.12500m);
        var askQuantity = new Quantity(0.00100m);

        var quote = new Quote(Id, EventTime, ReceivedTime, bidPrice, bidQuantity, askPrice, askQuantity);

        Assert.Equal(Id, quote.InstrumentId);
        Assert.Same(EventTime, quote.EventTime);
        Assert.Same(ReceivedTime, quote.ReceivedTime);
        Assert.Same(bidPrice, quote.BidPrice);
        Assert.Same(askPrice, quote.AskPrice);
        Assert.Same(bidQuantity, quote.BidQuantity);
        Assert.Same(askQuantity, quote.AskQuantity);
        Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(quote.BidPrice.Value));
        Assert.Equal(decimal.GetBits(10.0700m), decimal.GetBits(quote.AskPrice.Value));
        Assert.Equal(decimal.GetBits(0.12500m), decimal.GetBits(quote.BidQuantity.Value));
        Assert.Equal(decimal.GetBits(0.00100m), decimal.GetBits(quote.AskQuantity.Value));
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("EventTime")]
    [InlineData("ReceivedTime")]
    [InlineData("BidPrice")]
    [InlineData("BidQuantity")]
    [InlineData("AskPrice")]
    [InlineData("AskQuantity")]
    public void RejectsEachMissingRequiredComponent(string component)
    {
        Assert.Throws<DomainException>(() => new Quote(
            component == "InstrumentId" ? null : Id,
            component == "EventTime" ? null : EventTime,
            component == "ReceivedTime" ? null : ReceivedTime,
            component == "BidPrice" ? null : new Price(10m),
            component == "BidQuantity" ? null : new Quantity(1m),
            component == "AskPrice" ? null : new Price(11m),
            component == "AskQuantity" ? null : new Quantity(1m)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsZeroPriceOnEitherSide(bool zeroBid)
    {
        Assert.Throws<DomainException>(() => CreateQuote(bid: zeroBid ? 0m : 10m, ask: zeroBid ? 11m : 0m));
    }

    [Theory]
    [MemberData(nameof(SpreadCases))]
    public void AcceptsNormalAndLockedQuotesButRejectsCrossedQuotes(decimal bid, decimal ask, bool valid)
    {
        if (!valid)
        {
            Assert.Throws<DomainException>(() => CreateQuote(bid: bid, ask: ask));
            return;
        }

        var quote = CreateQuote(bid: bid, ask: ask);
        Assert.Equal(bid, quote.BidPrice.Value);
        Assert.Equal(ask, quote.AskPrice.Value);
    }

    [Theory]
    [MemberData(nameof(QuantityCases))]
    public void PreservesZeroAndFractionalQuantitiesOnBothSides(decimal bidQuantity, decimal askQuantity)
    {
        var quote = CreateQuote(bidQuantity: bidQuantity, askQuantity: askQuantity);

        Assert.Equal(decimal.GetBits(bidQuantity), decimal.GetBits(quote.BidQuantity.Value));
        Assert.Equal(decimal.GetBits(askQuantity), decimal.GetBits(quote.AskQuantity.Value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void PreservesBothTimesWithoutAssumingReceivedTimeOrdering(int receivedDeltaTicks)
    {
        var received = new UtcInstant(EventTime.Value.AddTicks(receivedDeltaTicks).ToOffset(TimeSpan.FromHours(-3)));

        var quote = new Quote(Id, EventTime, received, new Price(10m), new Quantity(1m),
            new Price(11m), new Quantity(1m));

        Assert.Same(EventTime, quote.EventTime);
        Assert.Same(received, quote.ReceivedTime);
        Assert.Equal(EventTime.Value.UtcTicks + receivedDeltaTicks, quote.ReceivedTime.Value.UtcTicks);
    }

    [Fact]
    public void StructuralEqualityAndHashingUseEquivalentComponentValues()
    {
        var first = CreateQuote();
        var second = new Quote(InstrumentId.From(Id.Value),
            new UtcInstant(EventTime.Value.ToOffset(TimeSpan.FromHours(-3))),
            new UtcInstant(ReceivedTime.Value.ToOffset(TimeSpan.FromHours(3))),
            new Price(10.00m), new Quantity(1.00m), new Price(11.00m), new Quantity(1.00m));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<Quote> { first, second });
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("EventTime")]
    [InlineData("ReceivedTime")]
    [InlineData("BidPrice")]
    [InlineData("BidQuantity")]
    [InlineData("AskPrice")]
    [InlineData("AskQuantity")]
    public void DifferentObservationComponentsMakeSnapshotsUnequal(string component)
    {
        var first = CreateQuote();
        var second = new Quote(
            component == "InstrumentId" ? InstrumentId.From(new Guid("12067718-caa9-4b98-996d-b73234605d9f")) : Id,
            component == "EventTime" ? new UtcInstant(EventTime.Value.AddTicks(1)) : EventTime,
            component == "ReceivedTime" ? new UtcInstant(ReceivedTime.Value.AddTicks(1)) : ReceivedTime,
            new Price(component == "BidPrice" ? 10.5m : 10m),
            new Quantity(component == "BidQuantity" ? 2m : 1m),
            new Price(component == "AskPrice" ? 11.5m : 11m),
            new Quantity(component == "AskQuantity" ? 2m : 1m));

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Theory]
    [MemberData(nameof(PreciseValues))]
    public void PreservesHighPrecisionAndExtremeDecimals(decimal value)
    {
        var quote = CreateQuote(bid: value, ask: value, bidQuantity: value, askQuantity: value);
        var bits = decimal.GetBits(value);

        Assert.Equal(bits, decimal.GetBits(quote.BidPrice.Value));
        Assert.Equal(bits, decimal.GetBits(quote.AskPrice.Value));
        Assert.Equal(bits, decimal.GetBits(quote.BidQuantity.Value));
        Assert.Equal(bits, decimal.GetBits(quote.AskQuantity.Value));
    }

    [Fact]
    public void SnapshotConstructionDoesNotValidateInstrumentIncrements()
    {
        var instrument = new Instrument(Id, new Symbol("TEST"), new Venue("CUSTOM"), AssetClass.Equity,
            new Currency("USD"), new Price(0.05m), new Quantity(100m));
        var quote = CreateQuote(bid: 10.07m, ask: 10.08m, bidQuantity: 0.001m, askQuantity: 0.005m);

        Assert.Equal(instrument.Id, quote.InstrumentId);
        Assert.False(instrument.IsValidPrice(quote.BidPrice));
        Assert.False(instrument.IsValidPrice(quote.AskPrice));
        Assert.False(instrument.IsValidQuantity(quote.BidQuantity));
        Assert.False(instrument.IsValidQuantity(quote.AskQuantity));
    }

    private static Quote CreateQuote(decimal bid = 10m, decimal ask = 11m,
        decimal bidQuantity = 1m, decimal askQuantity = 1m)
        => new(InstrumentId.From(Id.Value), EventTime, ReceivedTime,
            new Price(bid), new Quantity(bidQuantity), new Price(ask), new Quantity(askQuantity));
}
