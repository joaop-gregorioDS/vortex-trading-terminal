using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>Stateless decimal population Bollinger bands with a private bounded Newton square root.</summary>
public sealed class BollingerBands : IMultiSeriesIndicator
{
    private const decimal SquareRootTolerance = 0.000000000000000000000000001m;
    private const int SquareRootMaxIterations = 128;

    public string Name => "Bollinger Bands";
    public int Period { get; }
    public decimal StandardDeviationMultiplier { get; }

    public BollingerBands(int period, decimal standardDeviationMultiplier)
    {
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period), "Bollinger period must be positive.");
        if (standardDeviationMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(standardDeviationMultiplier), "Standard deviation multiplier must be positive.");

        Period = period;
        StandardDeviationMultiplier = standardDeviationMultiplier;
    }

    public MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        var middle = new SimpleMovingAverage(Period).Calculate(snapshot);
        var upper = new List<IndicatorPoint>(middle.Count);
        var lower = new List<IndicatorPoint>(middle.Count);
        for (var index = 0; index < middle.Count; index++)
        {
            var point = middle.Points[index];
            decimal squaredDeviationSum = 0;
            for (var offset = 0; offset < Period; offset++)
            {
                var deviation = snapshot[index + offset].Close.Value - point.Value;
                squaredDeviationSum += deviation * deviation;
            }

            var variance = squaredDeviationSum / Period;
            var sigma = SquareRoot(variance);
            var bandOffset = StandardDeviationMultiplier * sigma;
            upper.Add(new IndicatorPoint(point.Time, point.Value + bandOffset));
            lower.Add(new IndicatorPoint(point.Time, point.Value - bandOffset));
        }

        return new MultiSeriesIndicatorResult([
            new NamedIndicatorSeries("Middle", middle),
            new NamedIndicatorSeries("Upper", new IndicatorSeries(upper)),
            new NamedIndicatorSeries("Lower", new IndicatorSeries(lower))]);
    }

    private static decimal SquareRoot(decimal value)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(nameof(value), "Square root requires a nonnegative value.");
        if (value == 0m) return 0m;

        var current = value >= 1m ? value : 1m;
        decimal? previous = null;
        for (var iteration = 0; iteration < SquareRootMaxIterations; iteration++)
        {
            // Split the Newton average to avoid an unnecessary overflowing numerator sum.
            var next = current / 2m + (value / current) / 2m;
            if (Math.Abs(next - current) <= SquareRootTolerance) return next;
            if (previous.HasValue && next == previous.Value && current != next)
            {
                // Finite decimal precision can stabilize as A -> B -> A instead of a fixed point.
                var currentError = Math.Abs(current - value / current);
                var nextError = Math.Abs(next - value / next);
                if (currentError < nextError) return current;
                if (nextError < currentError) return next;
                return Math.Min(current, next); // Smaller candidate only breaks an exact error tie.
            }

            previous = current;
            current = next;
        }

        throw new ArithmeticException("Decimal square root did not converge within 128 iterations.");
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
