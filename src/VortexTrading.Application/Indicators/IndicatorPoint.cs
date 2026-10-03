using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Indicators;

/// <summary>An immutable indicator value at the close time that made it usable.</summary>
public sealed record IndicatorPoint
{
    public UtcInstant Time { get; }
    public decimal Value { get; }

    public IndicatorPoint(UtcInstant? time, decimal value)
    {
        ArgumentNullException.ThrowIfNull(time);
        Time = time;
        Value = value;
    }
}
