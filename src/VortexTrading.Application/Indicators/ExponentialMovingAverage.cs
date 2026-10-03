using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal EMA of candle closes, initialized with an SMA seed.</summary>
public sealed class ExponentialMovingAverage : IIndicator
{
    public string Name => "EMA";
    public int Period { get; }

    public ExponentialMovingAverage(int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "EMA period must be positive.");
        }

        Period = period;
    }

    public IndicatorSeries Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        if (snapshot.Length < Period) return new IndicatorSeries([]);

        decimal seedSum = 0;
        for (var index = 0; index < Period; index++) seedSum += snapshot[index].Close.Value;

        var ema = seedSum / Period;
        var points = new List<IndicatorPoint>(snapshot.Length - Period + 1)
        {
            new(snapshot[Period - 1].CloseTime, ema)
        };
        var alpha = 2m / (Period + 1m);
        for (var index = Period; index < snapshot.Length; index++)
        {
            ema = alpha * snapshot[index].Close.Value + (1m - alpha) * ema;
            points.Add(new IndicatorPoint(snapshot[index].CloseTime, ema));
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
