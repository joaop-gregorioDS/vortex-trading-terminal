using System.Globalization;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Risk.Rules;

/// <summary>
/// Rejects orders whose estimated financial notional value (Quantity * Price) exceeds a configured ceiling.
/// Protects account capital against oversized financial commitments.
/// </summary>
public sealed class MaxOrderNotionalRule : IRiskRule
{
    private readonly decimal _maxNotional;

    public MaxOrderNotionalRule(decimal maxNotional)
    {
        if (maxNotional <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNotional), "Maximum order notional must be positive.");
        }

        _maxNotional = maxNotional;
    }

    public string Name => "MaxOrderNotional";

    public decimal MaxNotional => _maxNotional;

    public RiskDecision Evaluate(RiskEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        decimal? price = context.Order.Type switch
        {
            OrderType.Limit or OrderType.Stop => context.Order.LimitPrice?.Value,
            OrderType.Market => context.ReferencePrice?.Value,
            _ => null
        };

        if (price is null)
        {
            return RiskDecision.Reject("Cannot evaluate order notional value because no price is available.");
        }

        if (context.PointValue <= 0m)
        {
            return RiskDecision.Reject(
                "Cannot evaluate order notional value because the instrument point value is not positive.");
        }

        var notional = context.Order.Quantity.Value * price.Value * context.PointValue;
        if (notional > _maxNotional)
        {
            return RiskDecision.Reject(
                string.Format(CultureInfo.InvariantCulture,
                    "Order estimated notional value {0:0.##} exceeds maximum allowed notional of {1:0.##}.",
                    notional, _maxNotional));
        }

        return RiskDecision.Approve();
    }
}
