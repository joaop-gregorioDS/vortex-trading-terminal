using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Risk.Rules;

/// <summary>
/// Rejects market orders when the session has no known reference price yet.
/// Prevents unpriced market execution into an uninitialized or unobservable market feed.
/// </summary>
public sealed class ReferencePriceForMarketOrdersRule : IRiskRule
{
    public string Name => "ReferencePriceForMarketOrders";

    public RiskDecision Evaluate(RiskEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Order.Type == OrderType.Market && context.ReferencePrice is null)
        {
            return RiskDecision.Reject("Market order rejected: no reference price is currently available for this market.");
        }

        return RiskDecision.Approve();
    }
}
