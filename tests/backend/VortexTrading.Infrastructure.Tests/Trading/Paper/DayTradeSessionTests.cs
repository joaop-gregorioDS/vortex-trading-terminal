using VortexTrading.Application.MarketData;
using VortexTrading.Application.Trading;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

/// <summary>Day-trade orders: shorts, flatten, reverse and the OCO bracket on the simulated tape.</summary>
public sealed class DayTradeSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BracketArmsAfterTheFillAndTheSamePrintDoesNotHitTheStop()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = session.Now();
        session.Publish(petr.InstrumentId, t0, 100m, 100m);
        var entry = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(2m), new Price(100m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(entry, Token);
        session.RememberProtection(entry, new ProtectionSpec(10m, 5m, ProtectionUnit.Points, null, null));

        // 80 would be through a stop at 95. The stop is born after this match, so the long stays open.
        session.Publish(petr.InstrumentId, After(t0, 1), 80m, 100m);

        Assert.Equal(OrderStatus.Filled, entry.Status);
        Assert.Equal(2m, Quantity(session, petr));
        var stop = Resting(session, OrderType.Stop);
        var target = Resting(session, OrderType.Limit);
        Assert.Equal(95m, stop.LimitPrice!.Value);
        Assert.Equal(110m, target.LimitPrice!.Value);
        Assert.Equal(OrderSide.Sell, stop.Side);
        Assert.Equal("stop", session.ProtectionRole(stop.Id));
        Assert.Equal("target", session.ProtectionRole(target.Id));

        session.Publish(petr.InstrumentId, After(t0, 2), 94m, 100m);

        Assert.Equal(OrderStatus.Filled, stop.Status);
        Assert.Equal(94m, Assert.Single(stop.Fills).Price.Value);
        Assert.Equal(OrderStatus.Cancelled, target.Status);
        Assert.Equal(0m, Quantity(session, petr));
        Assert.Equal(-12m, session.Portfolio().Positions[petr.InstrumentId].RealizedProfitAndLoss);
    }

    [Fact]
    public async Task TargetFillsAtItsPriceAndCancelsTheStop()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        await OpenLong(session, petr, 2m, 100m);
        var entry = session.Orders.Single(order => order.Side == OrderSide.Buy);
        session.RememberProtection(entry, new ProtectionSpec(10m, 5m, ProtectionUnit.Points, null, null));
        var armedAt = session.Now();
        session.Publish(petr.InstrumentId, After(armedAt, 1), 100m, 100m);

        var target = Resting(session, OrderType.Limit);
        var stop = Resting(session, OrderType.Stop);
        session.Publish(petr.InstrumentId, After(armedAt, 2), 110m, 100m);

        Assert.Equal(OrderStatus.Filled, target.Status);
        Assert.Equal(110m, Assert.Single(target.Fills).Price.Value);
        Assert.Equal(OrderStatus.Cancelled, stop.Status);
        Assert.Equal(0m, Quantity(session, petr));
        Assert.Equal(20m, session.Portfolio().Positions[petr.InstrumentId].RealizedProfitAndLoss);
    }

    [Fact]
    public async Task TrailAndBreakEvenMoveTheStopOnlyAfterThePrint()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        await OpenLong(session, petr, 1m, 100m);
        var entry = session.Orders.Single(order => order.Side == OrderSide.Buy);
        session.RememberProtection(entry, new ProtectionSpec(50m, 10m, ProtectionUnit.Points, 5m, 8m));
        var armedAt = session.Now();
        session.Publish(petr.InstrumentId, After(armedAt, 1), 100m, 100m);
        var stop = Resting(session, OrderType.Stop);
        Assert.Equal(90m, stop.LimitPrice!.Value);

        session.Publish(petr.InstrumentId, After(armedAt, 2), 107m, 100m);
        // Seven points is short of the break-even trigger. The 5-point trail still follows this later print.
        Assert.Equal(102m, stop.LimitPrice.Value);
        Assert.Equal(OrderStatus.Accepted, stop.Status);

        session.Publish(petr.InstrumentId, After(armedAt, 3), 110m, 100m);
        // Break-even at 100 does not beat 102. The trail pulls the stop to 105, and 110 does not fill it.
        Assert.Equal(105m, stop.LimitPrice.Value);
        Assert.Equal(1m, Quantity(session, petr));

        session.Publish(petr.InstrumentId, After(armedAt, 4), 104m, 100m);
        Assert.Equal(OrderStatus.Filled, stop.Status);
        Assert.Equal(104m, Assert.Single(stop.Fills).Price.Value);
        Assert.Equal(0m, Quantity(session, petr));
    }

    [Fact]
    public async Task ReverseFlipsTheHandAndFlattenDoesNotCancelARestingOrder()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        await OpenLong(session, petr, 2m, 100m);
        var resting = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Sell,
            new Quantity(1m), new Price(500m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(resting, Token);

        var closing = session.Flatten(petr.InstrumentId);
        Assert.NotNull(closing);
        Assert.Equal(OrderSide.Sell, closing.Side);
        Assert.Equal(2m, closing.Quantity.Value);
        Assert.Equal(OrderStatus.Accepted, resting.Status);

        var flatAt = session.Now();
        session.Publish(petr.InstrumentId, After(flatAt, 1), 100m, 100m);
        Assert.Equal(0m, Quantity(session, petr));
        Assert.Equal(OrderStatus.Accepted, resting.Status);

        await OpenLong(session, petr, 2m, 100m);
        var reverse = session.Reverse(petr.InstrumentId);
        Assert.NotNull(reverse);
        Assert.Equal(OrderSide.Sell, reverse.Side);
        Assert.Equal(4m, reverse.Quantity.Value);
        session.Publish(petr.InstrumentId, After(session.Now(), 1), 110m, 100m);
        Assert.Equal(-2m, Quantity(session, petr));

        var cover = session.Flatten(petr.InstrumentId);
        Assert.NotNull(cover);
        Assert.Equal(OrderSide.Buy, cover.Side);
        Assert.Equal(2m, cover.Quantity.Value);
        Assert.Equal(OrderStatus.Accepted, resting.Status);
    }

    [Fact]
    public void ReverseOfAFlatPositionSendsNothing()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");

        Assert.Null(session.Flatten(petr.InstrumentId));
        Assert.Null(session.Reverse(petr.InstrumentId));
        Assert.Empty(session.Orders);
    }

    [Fact]
    public async Task ReviseMovesThePriceWithoutFillingOnThePrintAlreadySeen()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var t0 = session.Now();
        session.Publish(petr.InstrumentId, t0, 100m, 100m);
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(90m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(order, Token);

        session.ReviseResting(order, 110m, 100m, petr.TickSize, petr.Decimals);

        Assert.Equal(OrderType.Stop, order.Type);
        Assert.Equal(110m, order.LimitPrice!.Value);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        session.Publish(petr.InstrumentId, After(t0, 1), 105m, 100m);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        session.Publish(petr.InstrumentId, After(t0, 2), 110m, 100m);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(110m, Assert.Single(order.Fills).Price.Value);
    }

    private static async Task OpenLong(PaperTradingSession session, AssetMarketProfile profile, decimal quantity, decimal price)
    {
        var opened = session.Now();
        session.Publish(profile.InstrumentId, opened, price, 100m);
        var buy = Order.CreateMarket(OrderId.From(Guid.NewGuid()), profile.InstrumentId, OrderSide.Buy,
            new Quantity(quantity), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(buy, Token);
        session.Publish(profile.InstrumentId, After(opened, 1), price, 100m);
        Assert.Equal(OrderStatus.Filled, buy.Status);
    }

    private static Order Resting(PaperTradingSession session, OrderType type) =>
        Assert.Single(session.Orders, order => order.Status == OrderStatus.Accepted && order.Type == type);

    private static decimal Quantity(PaperTradingSession session, AssetMarketProfile profile) =>
        session.Portfolio().Positions[profile.InstrumentId].Quantity;

    private static UtcInstant After(UtcInstant instant, int minutes) =>
        new(instant.Value.AddMinutes(minutes));
}
