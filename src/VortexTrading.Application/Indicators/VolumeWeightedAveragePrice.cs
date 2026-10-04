using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal H/L/C VWAP cumulative over the supplied candle batch.</summary>
public sealed class VolumeWeightedAveragePrice : IIndicator
{
    public string Name => "VWAP";

    public IndicatorSeries Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);

        decimal cumulativeWeightedPrice = 0;
        decimal cumulativeVolume = 0;
        var points = new List<IndicatorPoint>(snapshot.Length);
        foreach (var candle in snapshot)
        {
            var typicalPrice = TypicalPrice(candle);
            var volume = candle.Volume.Value;
            var weightedPrice = typicalPrice * volume;
            cumulativeWeightedPrice += weightedPrice;
            cumulativeVolume += volume;
            var value = cumulativeVolume == 0m
                ? typicalPrice
                : cumulativeWeightedPrice / cumulativeVolume;
            points.Add(new IndicatorPoint(candle.CloseTime, value));
        }

        return new IndicatorSeries(points);
    }

    private static decimal TypicalPrice(Candle candle)
    {
        var high = candle.High.Value;
        var low = candle.Low.Value;
        var close = candle.Close.Value;
        try
        {
            return (high + low + close) / 3m;
        }
        catch (OverflowException)
        {
            // Only the artificial H/L/C sum overflow is handled (ADR-026).
            return high / 3m + low / 3m + close / 3m;
        }
    }

    private static void Validate(Candle[] candles)
    {
        for (var index = 0; index < candles.Length; index++)
        {
            var candle = candles[index];
            if (candle is null)
            {
                throw new ArgumentException("Indicator input must not contain null candles.", nameof(candles));
            }

            if (index == 0) continue;
            if (candle.InstrumentId != candles[0].InstrumentId)
            {
                throw new ArgumentException("Indicator input must use one instrument.", nameof(candles));
            }

            if (candle.Interval != candles[0].Interval)
            {
                throw new ArgumentException("Indicator input must use one candle interval.", nameof(candles));
            }

            if (candle.OpenTime.CompareTo(candles[index - 1].OpenTime) <= 0)
            {
                throw new ArgumentException("Candle open times must be strictly increasing.", nameof(candles));
            }
        }
    }
}
