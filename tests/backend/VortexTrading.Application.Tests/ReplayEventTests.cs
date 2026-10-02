using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class ReplayEventTests
{
    private static readonly InstrumentId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Fact]
    public void QuoteRetainsItsTypedPayloadAndUsesEventTimeInsteadOfReceivedTime()
    {
        var received = new UtcInstant(Time.Value.AddHours(-1));
        var payload = new Quote(Id, Time, received, new Price(10.0500m), new Quantity(0.00100m), new Price(10.0600m), new Quantity(2m));
        var replayEvent = ReplayEvent.FromQuote(payload);

        Assert.Equal(ReplayEventKind.Quote, replayEvent.Kind);
        Assert.Same(payload, replayEvent.Quote);
        Assert.Null(replayEvent.Trade);
        Assert.Null(replayEvent.Candle);
        Assert.Same(Time, replayEvent.ReplayTime);
        Assert.Same(Id, replayEvent.InstrumentId);
        Assert.Same(received, Assert.IsType<Quote>(replayEvent.Quote).ReceivedTime);
    }

    [Fact]
    public void TradeRetainsItsTypedPayloadAndUsesEventTimeInsteadOfReceivedTime()
    {
        var received = new UtcInstant(Time.Value.AddHours(1));
        var payload = new Trade(Id, Time, received, new Price(7.1234567890123456789012345678m), new Quantity(0.00100m));
        var replayEvent = ReplayEvent.FromTrade(payload);

        Assert.Equal(ReplayEventKind.Trade, replayEvent.Kind);
        Assert.Same(payload, replayEvent.Trade);
        Assert.Null(replayEvent.Quote);
        Assert.Null(replayEvent.Candle);
        Assert.Same(Time, replayEvent.ReplayTime);
        Assert.Same(Id, replayEvent.InstrumentId);
        Assert.Same(received, Assert.IsType<Trade>(replayEvent.Trade).ReceivedTime);
    }

    [Fact]
    public void CandleRetainsItsTypedPayloadAndUsesOpenTimeInsteadOfCloseTime()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(170000003));
        var price = new Price(10.0500m);
        var payload = new Candle(Id, interval, Time, price, new Price(12m), new Price(9m), new Price(11m), new Quantity(0.00100m));
        var replayEvent = ReplayEvent.FromCandle(payload);

        Assert.Equal(ReplayEventKind.Candle, replayEvent.Kind);
        Assert.Same(payload, replayEvent.Candle);
        Assert.Null(replayEvent.Quote);
        Assert.Null(replayEvent.Trade);
        Assert.Same(Time, replayEvent.ReplayTime);
        Assert.NotEqual(payload.CloseTime, replayEvent.ReplayTime);
        Assert.Same(Id, replayEvent.InstrumentId);
        Assert.Same(interval, Assert.IsType<Candle>(replayEvent.Candle).Interval);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, "quote")]
    [InlineData(ReplayEventKind.Trade, "trade")]
    [InlineData(ReplayEventKind.Candle, "candle")]
    public void RejectsNullPayloads(ReplayEventKind kind, string parameterName)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(null),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(null),
            _ => ReplayEvent.FromCandle(null)
        });

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void KindOrderIsExplicitAndClosedToTheThreeCanonicalPayloadTypes()
    {
        Assert.Equal(0, (int)ReplayEventKind.Quote);
        Assert.Equal(1, (int)ReplayEventKind.Trade);
        Assert.Equal(2, (int)ReplayEventKind.Candle);
        Assert.Equal(new[] { ReplayEventKind.Quote, ReplayEventKind.Trade, ReplayEventKind.Candle }, Enum.GetValues<ReplayEventKind>());
    }

    [Fact]
    public void PublicConstructionCannotCreateEmptyOrMultiplePayloadStates()
    {
        Assert.True(typeof(ReplayEvent).IsSealed);
        Assert.Empty(typeof(ReplayEvent).GetConstructors());
        Assert.All(typeof(ReplayEvent).GetProperties(), property => Assert.False(property.CanWrite));
    }
}
