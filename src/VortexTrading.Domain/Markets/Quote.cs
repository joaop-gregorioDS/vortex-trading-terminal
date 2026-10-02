using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Markets;

public sealed record Quote
{
    public InstrumentId InstrumentId { get; }
    public UtcInstant EventTime { get; }
    public UtcInstant ReceivedTime { get; }
    public Price BidPrice { get; }
    public Quantity BidQuantity { get; }
    public Price AskPrice { get; }
    public Quantity AskQuantity { get; }

    public Quote(
        InstrumentId? instrumentId,
        UtcInstant? eventTime,
        UtcInstant? receivedTime,
        Price? bidPrice,
        Quantity? bidQuantity,
        Price? askPrice,
        Quantity? askQuantity)
    {
        if (instrumentId is null || eventTime is null || receivedTime is null
            || bidPrice is null || bidQuantity is null || askPrice is null || askQuantity is null)
        {
            throw new DomainException("Quote requires an instrument ID, event/received times and both bid/ask sides.");
        }

        if (bidPrice.Value <= 0 || askPrice.Value <= 0)
        {
            throw new DomainException("Quote bid and ask prices must be positive.");
        }

        if (bidPrice.Value > askPrice.Value)
        {
            throw new DomainException("Quote bid price must not exceed ask price.");
        }

        InstrumentId = instrumentId;
        EventTime = eventTime;
        ReceivedTime = receivedTime;
        BidPrice = bidPrice;
        BidQuantity = bidQuantity;
        AskPrice = askPrice;
        AskQuantity = askQuantity;
    }
}
