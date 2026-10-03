using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal Wilder RSI of candle closes, seeded from N price changes.</summary>
public sealed class RelativeStrengthIndex : IIndicator
{
    public string Name => "RSI";
    public int Period { get; }

    public RelativeStrengthIndex(int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "RSI period must be positive.");
        }

        Period = period;
    }

    public IndicatorSeries Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        if (snapshot.Length <= Period) return new IndicatorSeries([]);

        decimal gainSum = 0;
        decimal lossSum = 0;
        for (var index = 1; index <= Period; index++)
        {
            var (gain, loss) = Change(snapshot[index - 1], snapshot[index]);
            gainSum += gain;
            lossSum += loss;
        }

        var averageGain = gainSum / Period;
        var averageLoss = lossSum / Period;
        var points = new List<IndicatorPoint>(snapshot.Length - Period)
        {
            new(snapshot[Period].CloseTime, Value(averageGain, averageLoss))
        };
        var previousWeight = Period - 1m;
        for (var index = Period + 1; index < snapshot.Length; index++)
        {
            var (gain, loss) = Change(snapshot[index - 1], snapshot[index]);
            averageGain = (averageGain * previousWeight + gain) / Period;
            averageLoss = (averageLoss * previousWeight + loss) / Period;
            points.Add(new IndicatorPoint(snapshot[index].CloseTime, Value(averageGain, averageLoss)));
        }

        return new IndicatorSeries(points);
    }

    private static (decimal Gain, decimal Loss) Change(Candle previous, Candle current)
    {
        var change = current.Close.Value - previous.Close.Value;
        return (Math.Max(change, 0m), Math.Max(-change, 0m));
    }

    private static decimal Value(decimal averageGain, decimal averageLoss)
    {
        if (averageLoss == 0m) return averageGain == 0m ? 50m : 100m;
        if (averageGain == 0m) return 0m;

        var relativeStrength = averageGain / averageLoss;
        return 100m - 100m / (1m + relativeStrength);
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
