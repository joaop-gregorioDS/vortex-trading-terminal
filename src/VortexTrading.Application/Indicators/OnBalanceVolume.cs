using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless signed decimal OBV with a zero baseline for each supplied batch.</summary>
public sealed class OnBalanceVolume : IIndicator
{
    public string Name => "OBV";

    public IndicatorSeries Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);

        decimal balance = 0m;
        var points = new List<IndicatorPoint>(snapshot.Length);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var candle = snapshot[index];
            if (index > 0)
            {
                var previousClose = snapshot[index - 1].Close.Value;
                if (candle.Close.Value > previousClose)
                {
                    balance += candle.Volume.Value;
                }
                else if (candle.Close.Value < previousClose)
                {
                    balance -= candle.Volume.Value;
                }
            }

            points.Add(new IndicatorPoint(candle.CloseTime, balance));
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
