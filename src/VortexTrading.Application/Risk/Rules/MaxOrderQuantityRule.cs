using System.Globalization;

namespace VortexTrading.Application.Risk.Rules;

/// <summary>
/// Rejects orders whose requested quantity exceeds a configured maximum ceiling per single order.
/// Protects against 'fat-finger' data entry errors and unhedged volume spikes.
/// </summary>
public sealed class MaxOrderQuantityRule : IRiskRule
{
    private readonly decimal _maxQuantity;

    public MaxOrderQuantityRule(decimal maxQuantity)
    {
        if (maxQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQuantity), "Maximum order quantity must be positive.");
        }

        _maxQuantity = maxQuantity;
    }

    public string Name => "MaxOrderQuantity";

    public decimal MaxQuantity => _maxQuantity;

    public RiskDecision Evaluate(RiskEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Order.Quantity.Value > _maxQuantity)
        {
            return RiskDecision.Reject(
                string.Format(CultureInfo.InvariantCulture,
                    "Order quantity {0} exceeds maximum allowed order quantity of {1}.",
                    context.Order.Quantity.Value, _maxQuantity));
        }

        return RiskDecision.Approve();
    }
}
