using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Risk;

/// <summary>Context supplied to risk rules to evaluate an incoming order before submission.</summary>
/// <param name="Order">The created order intent being evaluated.</param>
/// <param name="Portfolio">Current portfolio holding positions derived from historical fills.</param>
/// <param name="ReferencePrice">Latest known reference price for the instrument, if available.</param>
/// <param name="ActiveOrders">Orders currently resting or partially filled in the book.</param>
/// <param name="PointValue">Money per price point for the order's instrument. Stocks use 1.</param>
public sealed record RiskEvaluationContext(
    Order Order,
    Portfolio Portfolio,
    Price? ReferencePrice,
    IReadOnlyCollection<Order> ActiveOrders,
    decimal PointValue = 1m);
