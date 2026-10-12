using VortexTrading.Application.Risk;
using VortexTrading.Application.Risk.Rules;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Risk;

public sealed class MaxOrderNotionalRuleTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Now = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ConstructorRejectsZeroOrNegativeLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxOrderNotionalRule(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxOrderNotionalRule(-100m));
    }

    [Fact]
    public void ApprovesLimitOrderWithNotionalWithinLimit()
    {
        var rule = new MaxOrderNotionalRule(1000m);
        // 10 * 100 = 1000
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(10m), new Price(100m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), null, []);

        var decision = rule.Evaluate(context);

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void RejectsLimitOrderWithNotionalExceedingLimit()
    {
        var rule = new MaxOrderNotionalRule(1000m);
        // 10.1 * 100 = 1010 > 1000
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(10.1m), new Price(100m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), null, []);

        var decision = rule.Evaluate(context);

        Assert.False(decision.IsApproved);
        Assert.Contains("exceeds maximum allowed notional", decision.Reason);
    }

    [Fact]
    public void EvaluatesMarketOrderUsingReferencePrice()
    {
        var rule = new MaxOrderNotionalRule(500m);
        // 5 * 100 = 500
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(5m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(100m), []);

        Assert.True(rule.Evaluate(context).IsApproved);

        // 6 * 100 = 600 > 500
        var largerOrder = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(6m), TimeInForce.GTC, Now);
        var largerContext = new RiskEvaluationContext(largerOrder, Portfolio.Empty(), new Price(100m), []);

        var decision = rule.Evaluate(largerContext);
        Assert.False(decision.IsApproved);
        Assert.Contains("exceeds maximum allowed notional", decision.Reason);
    }

    [Fact]
    public void RejectsMarketOrderWhenReferencePriceIsAbsent()
    {
        var rule = new MaxOrderNotionalRule(500m);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(5m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), null, []);

        var decision = rule.Evaluate(context);

        Assert.False(decision.IsApproved);
        Assert.Contains("no price is available", decision.Reason);
    }

    [Fact]
    public void EvaluatesStopOrderUsingItsTriggerInsteadOfTheReferencePrice()
    {
        var rule = new MaxOrderNotionalRule(1000m);
        var order = Order.CreateStop(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), new Price(100m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(10000m), []);

        Assert.True(rule.Evaluate(context).IsApproved);
    }

    [Fact]
    public void AppliesTheInstrumentPointValue()
    {
        var rule = new MaxOrderNotionalRule(50000m);
        // 1 contract * 208785 points * 0.20 = 41757, inside the ceiling.
        var one = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(1m), new Price(208785m), TimeInForce.GTC, Now);
        Assert.True(rule.Evaluate(new RiskEvaluationContext(one, Portfolio.Empty(), null, [], 0.20m)).IsApproved);

        // 2 * 208785 * 0.20 = 83514, over the ceiling. Without the point value, one contract would already fail.
        var two = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(2m), new Price(208785m), TimeInForce.GTC, Now);
        var decision = rule.Evaluate(new RiskEvaluationContext(two, Portfolio.Empty(), null, [], 0.20m));

        Assert.False(decision.IsApproved);
        Assert.Contains("exceeds maximum allowed notional", decision.Reason);
    }
}
