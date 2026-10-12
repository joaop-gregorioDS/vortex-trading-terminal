using VortexTrading.Application.Risk;
using VortexTrading.Application.Risk.Rules;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Risk;

public sealed class MaxPositionQuantityRuleTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Now = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ConstructorRejectsZeroOrNegativeLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxPositionQuantityRule(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxPositionQuantityRule(-50m));
    }

    [Fact]
    public void SellOrderAlwaysApprovedEvenIfPositionIsAtOrAboveLimit()
    {
        var rule = new MaxPositionQuantityRule(100m);
        // Selling reduces long exposure
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Sell, new Quantity(50m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(100m), []);

        var decision = rule.Evaluate(context);

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void ApprovesBuyWhenProjectedHeldAndPendingWithinLimit()
    {
        var rule = new MaxPositionQuantityRule(100m);

        // Portfolio with 30 units held
        var fill = new OrderFill(ExecutionId.From(Guid.NewGuid()), OrderId.From(Guid.NewGuid()), new Quantity(30m), new Price(100m), Now);
        var portfolio = Portfolio.Empty().Apply(Instrument, fill, OrderSide.Buy).Portfolio;

        // Active pending buy order with 20 units
        var activeOrder = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(20m), new Price(99m), TimeInForce.GTC, Now);
        activeOrder.Accept(Now);

        // New buy order of 40 units (30 held + 20 pending + 40 new = 90 <= 100)
        var newOrder = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(40m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(newOrder, portfolio, new Price(100m), [activeOrder]);

        var decision = rule.Evaluate(context);

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void RejectsBuyWhenProjectedHeldAndPendingExceedsLimit()
    {
        var rule = new MaxPositionQuantityRule(100m);

        // Portfolio with 60 units held
        var fill = new OrderFill(ExecutionId.From(Guid.NewGuid()), OrderId.From(Guid.NewGuid()), new Quantity(60m), new Price(100m), Now);
        var portfolio = Portfolio.Empty().Apply(Instrument, fill, OrderSide.Buy).Portfolio;

        // Active pending buy order with 20 units
        var activeOrder = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(20m), new Price(99m), TimeInForce.GTC, Now);
        activeOrder.Accept(Now);

        // New buy order of 25 units (60 held + 20 pending + 25 new = 105 > 100)
        var newOrder = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(25m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(newOrder, portfolio, new Price(100m), [activeOrder]);

        var decision = rule.Evaluate(context);

        Assert.False(decision.IsApproved);
        Assert.Contains("exceeds maximum allowed position limit", decision.Reason);
        Assert.Contains("held: 60", decision.Reason);
        Assert.Contains("pending: 20", decision.Reason);
    }

    [Fact]
    public void RejectsSellThatWouldOpenAShortBeyondTheLimit()
    {
        var rule = new MaxPositionQuantityRule(100m);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Sell,
            new Quantity(150m), TimeInForce.GTC, Now);
        var decision = rule.Evaluate(new RiskEvaluationContext(order, Portfolio.Empty(), new Price(100m), []));

        Assert.False(decision.IsApproved);
        Assert.Contains("exceeds maximum allowed position limit", decision.Reason);
    }

    [Fact]
    public void ApprovesSellThatOnlyReducesALong()
    {
        var rule = new MaxPositionQuantityRule(100m);
        var fill = new OrderFill(ExecutionId.From(Guid.NewGuid()), OrderId.From(Guid.NewGuid()),
            new Quantity(80m), new Price(100m), Now);
        var portfolio = Portfolio.Empty().Apply(Instrument, fill, OrderSide.Buy).Portfolio;
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Sell,
            new Quantity(30m), TimeInForce.GTC, Now);

        Assert.True(rule.Evaluate(new RiskEvaluationContext(order, portfolio, new Price(100m), [])).IsApproved);
    }
}
