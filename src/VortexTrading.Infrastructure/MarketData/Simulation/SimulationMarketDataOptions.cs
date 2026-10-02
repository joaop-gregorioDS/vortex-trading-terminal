using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.MarketData.Simulation;

public sealed record SimulationMarketDataOptions
{
    public UtcInstant StartTime { get; }
    public Price Price { get; }
    public Quantity Quantity { get; }
    public int EventCount { get; }

    public SimulationMarketDataOptions(UtcInstant? startTime, Price? price, Quantity? quantity, int eventCount)
    {
        ArgumentNullException.ThrowIfNull(startTime);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);

        if (price.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Simulation price must be positive.");
        }

        if (quantity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Simulation quantity must be positive.");
        }

        if (eventCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount), "Simulation event count per instrument must be positive.");
        }

        var lastOffsetTicks = (long)(eventCount - 1) * TimeSpan.TicksPerSecond;
        if (lastOffsetTicks > DateTimeOffset.MaxValue.UtcTicks - startTime.Value.UtcTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount), "Simulation quote/trade times must be representable.");
        }

        StartTime = startTime;
        Price = price;
        Quantity = quantity;
        EventCount = eventCount;
    }
}
