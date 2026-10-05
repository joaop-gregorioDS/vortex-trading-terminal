namespace VortexTrading.Domain.Trading;

public enum OrderStatus
{
    Created,
    Accepted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected
}
