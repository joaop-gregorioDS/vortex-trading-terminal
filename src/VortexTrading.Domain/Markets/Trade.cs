using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Markets;

public sealed record Trade
{
    public InstrumentId InstrumentId { get; }
    public UtcInstant EventTime { get; }
    public UtcInstant ReceivedTime { get; }
    public Price Price { get; }
    public Quantity Quantity { get; }

    public Trade(
        InstrumentId? instrumentId,
        UtcInstant? eventTime,
        UtcInstant? receivedTime,
        Price? price,
        Quantity? quantity)
    {
        if (instrumentId is null || eventTime is null || receivedTime is null
            || price is null || quantity is null)
        {
            throw new DomainException("Trade requires an instrument ID, event/received times, price and quantity.");
        }

        if (price.Value <= 0)
        {
            throw new DomainException("Trade price must be positive.");
        }

        if (quantity.Value <= 0)
        {
            throw new DomainException("Trade quantity must be positive.");
        }

        InstrumentId = instrumentId;
        EventTime = eventTime;
        ReceivedTime = receivedTime;
        Price = price;
        Quantity = quantity;
    }
}
