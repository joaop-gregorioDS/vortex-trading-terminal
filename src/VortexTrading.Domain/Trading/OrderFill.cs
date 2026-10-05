using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Domain.Trading;

public sealed record OrderFill
{
    public ExecutionId ExecutionId { get; }
    public OrderId OrderId { get; }
    public Quantity Quantity { get; }
    public Price Price { get; }
    public UtcInstant ExecutedAt { get; }

    public OrderFill(ExecutionId? executionId, OrderId? orderId, Quantity? quantity, Price? price, UtcInstant? executedAt)
    {
        if (executionId is null || orderId is null || quantity is null || price is null || executedAt is null)
        {
            throw new DomainException("Order fill requires execution ID, order ID, quantity, price and execution time.");
        }

        if (quantity.Value <= 0 || price.Value <= 0)
        {
            throw new DomainException("Order fill quantity and price must be positive.");
        }

        ExecutionId = executionId;
        OrderId = orderId;
        Quantity = quantity;
        Price = price;
        ExecutedAt = executedAt;
    }
}
