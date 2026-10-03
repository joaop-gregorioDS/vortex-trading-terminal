using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal Slow Stochastic with private raw values and independent public warm-ups.</summary>
public sealed class StochasticOscillator : IMultiSeriesIndicator
{
    public string Name => "Stochastic Oscillator";
    public int KPeriod { get; }
    public int KSmoothingPeriod { get; }
    public int DPeriod { get; }

    public StochasticOscillator(int kPeriod, int kSmoothingPeriod, int dPeriod)
    {
        if (kPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(kPeriod), "Raw stochastic period must be positive.");
        if (kSmoothingPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(kSmoothingPeriod), "Stochastic K smoothing period must be positive.");
        if (dPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(dPeriod), "Stochastic D period must be positive.");

        KPeriod = kPeriod;
        KSmoothingPeriod = kSmoothingPeriod;
        DPeriod = dPeriod;
    }

    public MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        var rawPoints = new List<IndicatorPoint>(Math.Max(0, snapshot.Length - KPeriod + 1));
        for (var index = KPeriod - 1; index < snapshot.Length; index++)
        {
            var start = index - KPeriod + 1;
            var highest = snapshot[start].High.Value;
            var lowest = snapshot[start].Low.Value;
            for (var offset = 1; offset < KPeriod; offset++)
            {
                var candle = snapshot[start + offset];
                if (candle.High.Value > highest) highest = candle.High.Value;
                if (candle.Low.Value < lowest) lowest = candle.Low.Value;
            }

            var range = highest - lowest;
            // Normalize first: multiplying a large numerator by 100 could overflow unnecessarily.
            var raw = range == 0m ? 50m : ((snapshot[index].Close.Value - lowest) / range) * 100m;
            rawPoints.Add(new IndicatorPoint(snapshot[index].CloseTime, raw));
        }

        var slowK = Smooth(rawPoints, KSmoothingPeriod);
        var d = Smooth(slowK.Points, DPeriod);
        return new MultiSeriesIndicatorResult([
            new NamedIndicatorSeries("%K", slowK),
            new NamedIndicatorSeries("%D", d)]);
    }

    private static IndicatorSeries Smooth(IReadOnlyList<IndicatorPoint> input, int period)
    {
        if (input.Count < period) return new IndicatorSeries([]);
        var points = new List<IndicatorPoint>(input.Count - period + 1);
        for (var index = period - 1; index < input.Count; index++)
        {
            // Sum only this window, avoiding decimal subtraction drift from an outgoing window.
            decimal sum = 0;
            var start = index - period + 1;
            for (var offset = 0; offset < period; offset++) sum += input[start + offset].Value;
            points.Add(new IndicatorPoint(input[index].Time, sum / period));
        }

        return new IndicatorSeries(points);
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
