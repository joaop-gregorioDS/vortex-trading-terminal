using System.Globalization;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Risk.Rules;

/// <summary>
/// Rejects orders that would push total exposure (held position + pending open orders) beyond a configured maximum quantity.
/// Prevents accumulating excessive inventory in a single instrument.
/// </summary>
public sealed class MaxPositionQuantityRule : IRiskRule
{
    private readonly decimal _maxPositionQuantity;

    public MaxPositionQuantityRule(decimal maxPositionQuantity)
    {
        if (maxPositionQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPositionQuantity), "Maximum position quantity must be positive.");
        }

        _maxPositionQuantity = maxPositionQuantity;
    }

    public string Name => "MaxPositionQuantity";

    public decimal MaxPositionQuantity => _maxPositionQuantity;

    public RiskDecision Evaluate(RiskEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var currentQuantity = context.Portfolio.Positions.TryGetValue(context.Order.InstrumentId, out var position)
            ? position.Quantity
            : 0m;
        var sameSide = context.ActiveOrders
            .Where(o => o.InstrumentId == context.Order.InstrumentId
                && o.Side == context.Order.Side
                && o.Id != context.Order.Id)
            .Sum(o => o.RemainingQuantity.Value);

        // A buy can extend a long. A sell can extend a short. Reducing the open side stays inside the limit.
        var projectedQuantity = context.Order.Side == OrderSide.Buy
            ? currentQuantity + sameSide + context.Order.Quantity.Value
            : currentQuantity - sameSide - context.Order.Quantity.Value;
        var breaches = context.Order.Side == OrderSide.Buy
            ? projectedQuantity > _maxPositionQuantity
            : projectedQuantity < -_maxPositionQuantity;
        if (!breaches)
        {
            return RiskDecision.Approve();
        }

        return RiskDecision.Reject(
            string.Format(CultureInfo.InvariantCulture,
                "Projected position quantity {0} (held: {1}, pending: {2}, order: {3}) exceeds maximum allowed position limit of {4}.",
                projectedQuantity, currentQuantity, sameSide, context.Order.Quantity.Value, _maxPositionQuantity));
    }
}
