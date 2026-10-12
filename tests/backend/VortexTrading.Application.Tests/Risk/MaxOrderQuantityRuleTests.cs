using VortexTrading.Application.Risk;
using VortexTrading.Application.Risk.Rules;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Risk;

public sealed class MaxOrderQuantityRuleTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Now = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ConstructorRejectsZeroOrNegativeLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxOrderQuantityRule(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaxOrderQuantityRule(-10m));
    }

    [Fact]
    public void ApprovesOrderWithQuantityBelowOrEqualLimit()
    {
        var rule = new MaxOrderQuantityRule(100m);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(100m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(50m), []);

        var decision = rule.Evaluate(context);

        Assert.True(decision.IsApproved);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void RejectsOrderWithQuantityAboveLimit()
    {
        var rule = new MaxOrderQuantityRule(100m);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(100.0001m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(50m), []);

        var decision = rule.Evaluate(context);

        Assert.False(decision.IsApproved);
        Assert.NotNull(decision.Reason);
        Assert.Contains("exceeds maximum allowed order quantity", decision.Reason);
    }
}
