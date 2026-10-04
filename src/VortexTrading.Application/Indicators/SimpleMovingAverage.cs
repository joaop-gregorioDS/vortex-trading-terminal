using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal moving average of closed candle close prices.</summary>
public sealed class SimpleMovingAverage : IIndicator
{
    public string Name => "SMA";
    public int Period { get; }

    public SimpleMovingAverage(int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "SMA period must be positive.");
        }

        Period = period;
    }

    public IndicatorSeries Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        if (snapshot.Length < Period) return new IndicatorSeries([]);

        var points = new List<IndicatorPoint>(snapshot.Length - Period + 1);
        for (var index = Period - 1; index < snapshot.Length; index++)
        {
            // Recompute each window so discarded closes leave no historical rounding residue.
            decimal sum = 0m;
            for (var windowIndex = index - Period + 1; windowIndex <= index; windowIndex++)
            {
                sum += snapshot[windowIndex].Close.Value;
            }

            points.Add(new IndicatorPoint(snapshot[index].CloseTime, sum / Period));
        }

        return new IndicatorSeries(points);
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
