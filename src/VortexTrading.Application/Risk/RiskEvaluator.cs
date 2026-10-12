using VortexTrading.Application.Risk.Rules;

namespace VortexTrading.Application.Risk;

/// <summary>Default composite pre-trade risk evaluator.</summary>
public sealed class RiskEvaluator : IRiskEvaluator
{
    private readonly IReadOnlyList<IRiskRule> _rules;

    public RiskEvaluator(IEnumerable<IRiskRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToArray();
    }

    /// <summary>Active rules evaluated by this instance.</summary>
    public IReadOnlyList<IRiskRule> Rules => _rules;

    public RiskDecision Evaluate(RiskEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in _rules)
        {
            var decision = rule.Evaluate(context);
            if (!decision.IsApproved)
            {
                return decision;
            }
        }

        return RiskDecision.Approve();
    }

    /// <summary>Constructs a <see cref="RiskEvaluator"/> based on <see cref="RiskOptions"/> configuration.</summary>
    public static RiskEvaluator FromOptions(RiskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return new RiskEvaluator([]);
        }

        var rules = new List<IRiskRule>();

        if (options.RequireReferencePriceForMarketOrders)
        {
            rules.Add(new ReferencePriceForMarketOrdersRule());
        }

        if (options.MaxOrderQuantity is { } maxQuantity)
        {
            rules.Add(new MaxOrderQuantityRule(maxQuantity));
        }

        if (options.MaxOrderNotional is { } maxNotional)
        {
            rules.Add(new MaxOrderNotionalRule(maxNotional));
        }

        if (options.MaxPositionQuantity is { } maxPosition)
        {
            rules.Add(new MaxPositionQuantityRule(maxPosition));
        }

        return new RiskEvaluator(rules);
    }
}
