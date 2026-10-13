using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

public sealed class YahooMarketRoundTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AClosedRoundFreezesTheTapeAndALaterPriceDoesNotTrade()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);

        session.ApplyMarketRound(Round(false, petr.Ticker, 30m, t0));
        Assert.False(session.MarketOpen);
        Assert.Equal(30m, session.LastPriceFor(petr.InstrumentId)!.Value);
        Assert.Single(session.BarsFor(petr.InstrumentId));

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(order, Token);

        session.ApplyMarketRound(Round(false, petr.Ticker, 20m, t0.AddMinutes(1)));

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(30m, session.LastPriceFor(petr.InstrumentId)!.Value);
        Assert.Single(session.BarsFor(petr.InstrumentId));
        Assert.Empty(session.ObservationsFor(petr.InstrumentId));
    }

    [Fact]
    public async Task AnOpenRoundArmsAndTheNextPrintFills()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);

        session.ApplyMarketRound(Round(true, petr.Ticker, 30m, t0));
        Assert.True(session.MarketOpen);
        Assert.Empty(session.ObservationsFor(petr.InstrumentId));

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(order, Token);
        Assert.Equal(OrderStatus.Accepted, order.Status);

        session.ApplyMarketRound(Round(true, petr.Ticker, 31m, t0.AddMinutes(1)));

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(31m, order.Fills[0].Price.Value);
        Assert.Equal(31m, session.LastPriceFor(petr.InstrumentId)!.Value);
    }

    [Fact]
    public async Task ReopeningArmsAgainInsteadOfFillingOnTheFirstPrint()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);
        session.ApplyMarketRound(Round(false, petr.Ticker, 30m, t0));

        var reopen = t0.AddDays(1);
        session.ApplyMarketRound(Round(true, petr.Ticker, 28m, reopen));
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(28m, session.LastPriceFor(petr.InstrumentId)!.Value);

        session.ApplyMarketRound(Round(true, petr.Ticker, 28m, reopen.AddMinutes(1)));
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public void ALaterRoundKeepsTheEarlierMinute()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);

        session.ApplyMarketRound(Round(true, petr.Ticker, 30m, t0));
        session.ApplyMarketRound(Round(true, petr.Ticker, 31m, t0.AddMinutes(1)));

        var bars = session.BarsFor(petr.InstrumentId);
        Assert.Equal(2, bars.Count);
        Assert.Equal(30m, bars[0].Close);
        Assert.Equal(31m, bars[1].Close);
    }

    [Fact]
    public void RestoredMinutesStayWhenAnOlderRoundArrives()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var later = new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);
        session.RestoreMinuteBars(new Dictionary<InstrumentId, IReadOnlyList<SessionBar>>
        {
            [petr.InstrumentId] = [new SessionBar(new UtcInstant(later), 40m, 41m, 39m, 40m, 1m)]
        });

        session.ApplyMarketRound(Round(true, petr.Ticker, 30m, later.AddMinutes(-30)));

        var bars = session.BarsFor(petr.InstrumentId);
        Assert.Equal(2, bars.Count);
        Assert.Equal(40m, session.LastPriceFor(petr.InstrumentId)!.Value);
    }

    [Fact]
    public void ResetClearsTheYahooTape()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        session.ApplyMarketRound(Round(true, petr.Ticker, 30m, new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero)));

        session.ResetDemonstration();

        Assert.Null(session.MarketOpen);
        Assert.Null(session.CurrentTime);
        Assert.Empty(session.BarsFor(petr.InstrumentId));
        Assert.Null(session.LastPriceFor(petr.InstrumentId));
    }

    private static MarketRound Round(bool open, string ticker, decimal price, DateTimeOffset time)
    {
        var bar = new SessionBar(new UtcInstant(time), price, price + 1m, price - 1m, price, 10m);
        return new MarketRound(open, [new InstrumentMarketUpdate(ticker, price, time, [bar])]);
    }
}
