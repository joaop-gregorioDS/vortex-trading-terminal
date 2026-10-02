using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Markets;

public sealed record Candle
{
    public InstrumentId InstrumentId { get; }
    public CandleInterval Interval { get; }
    public UtcInstant OpenTime { get; }
    public Price Open { get; }
    public Price High { get; }
    public Price Low { get; }
    public Price Close { get; }
    public Quantity Volume { get; }

    public UtcInstant CloseTime => new(OpenTime.Value.Add(Interval.Duration));

    public Candle(
        InstrumentId? instrumentId,
        CandleInterval? interval,
        UtcInstant? openTime,
        Price? open,
        Price? high,
        Price? low,
        Price? close,
        Quantity? volume)
    {
        if (instrumentId is null || interval is null || openTime is null
            || open is null || high is null || low is null || close is null || volume is null)
        {
            throw new DomainException("Candle requires an instrument ID, interval, open time, OHLC prices and volume.");
        }

        if (open.Value <= 0 || high.Value <= 0 || low.Value <= 0 || close.Value <= 0)
        {
            throw new DomainException("Candle OHLC prices must be positive.");
        }

        if (high.Value < open.Value || high.Value < close.Value
            || low.Value > open.Value || low.Value > close.Value || high.Value < low.Value)
        {
            throw new DomainException("Candle OHLC prices must lie within the low/high range.");
        }

        if (interval.Duration.Ticks > DateTimeOffset.MaxValue.UtcTicks - openTime.Value.UtcTicks)
        {
            throw new DomainException("Candle interval must produce a representable close time.");
        }

        InstrumentId = instrumentId;
        Interval = interval;
        OpenTime = openTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}
