using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal Wilder ATR seeded from N candle true ranges.</summary>
public sealed class AverageTrueRange : IIndicator
{
    public string Name => "ATR";
    public int Period { get; }

    public AverageTrueRange(int period)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "ATR period must be positive.");
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
        for (var index = 0; index < Period; index++)
        {
            seedSum += TrueRange(snapshot[index], index == 0 ? null : snapshot[index - 1]);
        }

        var atr = seedSum / Period;
        var points = new List<IndicatorPoint>(snapshot.Length - Period + 1)
        {
            new(snapshot[Period - 1].CloseTime, atr)
        };
        var previousWeight = Period - 1m;
        for (var index = Period; index < snapshot.Length; index++)
        {
            var range = TrueRange(snapshot[index], snapshot[index - 1]);
            atr = (atr * previousWeight + range) / Period;
            points.Add(new IndicatorPoint(snapshot[index].CloseTime, atr));
        }

        return new IndicatorSeries(points);
    }

    private static decimal TrueRange(Candle candle, Candle? previous)
    {
        var intrabarRange = candle.High.Value - candle.Low.Value;
        if (previous is null) return intrabarRange;

        var highGap = Math.Abs(candle.High.Value - previous.Close.Value);
        var lowGap = Math.Abs(candle.Low.Value - previous.Close.Value);
        return Math.Max(intrabarRange, Math.Max(highGap, lowGap));
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
