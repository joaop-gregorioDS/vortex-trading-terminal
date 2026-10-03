using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal MACD with SMA-seeded EMAs and independent component warm-ups.</summary>
public sealed class MovingAverageConvergenceDivergence : IMultiSeriesIndicator
{
    public string Name => "MACD";
    public int FastPeriod { get; }
    public int SlowPeriod { get; }
    public int SignalPeriod { get; }

    public MovingAverageConvergenceDivergence(int fastPeriod, int slowPeriod, int signalPeriod)
    {
        if (fastPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(fastPeriod), "Fast EMA period must be positive.");
        if (slowPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(slowPeriod), "Slow EMA period must be positive.");
        if (signalPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(signalPeriod), "Signal EMA period must be positive.");
        if (fastPeriod >= slowPeriod)
            throw new ArgumentException("Fast EMA period must be less than slow EMA period.", nameof(fastPeriod));

        FastPeriod = fastPeriod;
        SlowPeriod = slowPeriod;
        SignalPeriod = signalPeriod;
    }

    public MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        var macdPoints = new List<IndicatorPoint>();
        var signalPoints = new List<IndicatorPoint>();
        var histogramPoints = new List<IndicatorPoint>();
        if (snapshot.Length >= SlowPeriod)
        {
            var fast = new ExponentialMovingAverage(FastPeriod).Calculate(snapshot);
            var slow = new ExponentialMovingAverage(SlowPeriod).Calculate(snapshot);
            var fastOffset = SlowPeriod - FastPeriod;
            var alpha = 2m / (SignalPeriod + 1m);
            decimal signalSeedSum = 0;
            decimal signal = 0;
            for (var index = 0; index < slow.Count; index++)
            {
                // Both EMA points belong to the same supplied candle's CloseTime.
                var time = slow.Points[index].Time;
                var value = fast.Points[index + fastOffset].Value - slow.Points[index].Value;
                macdPoints.Add(new IndicatorPoint(time, value));
                if (slow.Count < SignalPeriod) continue;

                if (index < SignalPeriod)
                {
                    signalSeedSum += value;
                    if (index != SignalPeriod - 1) continue;
                    signal = signalSeedSum / SignalPeriod;
                }
                else
                {
                    // Same Vortex EMA policy, applied to MACD values without fabricated candles.
                    signal = alpha * value + (1m - alpha) * signal;
                }

                signalPoints.Add(new IndicatorPoint(time, signal));
                histogramPoints.Add(new IndicatorPoint(time, value - signal));
            }
        }

        return new MultiSeriesIndicatorResult([
            new NamedIndicatorSeries("MACD", new IndicatorSeries(macdPoints)),
            new NamedIndicatorSeries("Signal", new IndicatorSeries(signalPoints)),
            new NamedIndicatorSeries("Histogram", new IndicatorSeries(histogramPoints))]);
    }

    private static void Validate(Candle[] candles)
    {
        for (var index = 0; index < candles.Length; index++)
        {
            var candle = candles[index];
            if (candle is null)
                throw new ArgumentException("Indicator input must not contain null candles.", nameof(candles));
            if (index == 0) continue;
            if (candle.InstrumentId != candles[0].InstrumentId)
                throw new ArgumentException("Indicator input must use one instrument.", nameof(candles));
            if (candle.Interval != candles[0].Interval)
                throw new ArgumentException("Indicator input must use one candle interval.", nameof(candles));
            if (candle.OpenTime.CompareTo(candles[index - 1].OpenTime) <= 0)
                throw new ArgumentException("Candle open times must be strictly increasing.", nameof(candles));
        }
    }
}
