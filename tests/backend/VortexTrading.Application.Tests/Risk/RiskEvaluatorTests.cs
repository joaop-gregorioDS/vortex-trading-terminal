using VortexTrading.Application.Risk;
using VortexTrading.Application.Risk.Rules;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Risk;

public sealed class RiskEvaluatorTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Now = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void EmptyRulesEvaluatorApprovesEverything()
    {
        var evaluator = new RiskEvaluator([]);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(10000m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), null, []);

        var decision = evaluator.Evaluate(context);

        Assert.True(decision.IsApproved);
    }

    [Fact]
    public void ShortCircuitsOnFirstFailingRule()
    {
        var orderQuantityRule = new MaxOrderQuantityRule(100m);
        var notionalRule = new MaxOrderNotionalRule(500m);

        var evaluator = new RiskEvaluator([orderQuantityRule, notionalRule]);

        // Order fails quantity check (150 > 100) and also notional check (150 * 10 = 1500 > 500)
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy, new Quantity(150m), new Price(10m), TimeInForce.GTC, Now);
        var context = new RiskEvaluationContext(order, Portfolio.Empty(), new Price(10m), []);

        var decision = evaluator.Evaluate(context);

        Assert.False(decision.IsApproved);
        // The first rule was order quantity, so it should report that reason
        Assert.Contains("exceeds maximum allowed order quantity", decision.Reason);
    }

    [Fact]
    public void FromOptionsConstructsConfiguredRules()
    {
        var options = new RiskOptions
        {
            Enabled = true,
            MaxOrderQuantity = 50m,
            MaxOrderNotional = 1000m,
            MaxPositionQuantity = 200m,
            RequireReferencePriceForMarketOrders = true
        };

        var evaluator = RiskEvaluator.FromOptions(options);

        Assert.Equal(4, evaluator.Rules.Count);
        Assert.Contains(evaluator.Rules, r => r is ReferencePriceForMarketOrdersRule);
        Assert.Contains(evaluator.Rules, r => r is MaxOrderQuantityRule);
        Assert.Contains(evaluator.Rules, r => r is MaxOrderNotionalRule);
        Assert.Contains(evaluator.Rules, r => r is MaxPositionQuantityRule);
    }

    [Fact]
    public void FromOptionsWithDisabledProducesEmptyRules()
    {
        var options = new RiskOptions
        {
            Enabled = false,
            MaxOrderQuantity = 50m
        };

        var evaluator = RiskEvaluator.FromOptions(options);

        Assert.Empty(evaluator.Rules);
    }
}
