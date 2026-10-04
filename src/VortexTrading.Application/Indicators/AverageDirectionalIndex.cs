using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>A stateless decimal Wilder ADX with public directional indices and private DX.</summary>
public sealed class AverageDirectionalIndex : IMultiSeriesIndicator
{
    public string Name => "ADX";
    public int Period { get; }

    public AverageDirectionalIndex(int period)
    {
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period), "ADX period must be positive.");

        Period = period;
    }

    public MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        var snapshot = candles.ToArray();
        Validate(snapshot);
        var diCount = Math.Max(0, snapshot.Length - Period);
        var plusPoints = new List<IndicatorPoint>(diCount);
        var minusPoints = new List<IndicatorPoint>(diCount);
        var adxPoints = new List<IndicatorPoint>(Math.Max(0, diCount - Period + 1));
        if (diCount == 0) return Result(plusPoints, minusPoints, adxPoints);

        decimal smoothedTr = 0;
        decimal smoothedPlusDm = 0;
        decimal smoothedMinusDm = 0;
        decimal dxSeedSum = 0;
        decimal adx = 0;
        var dxSeedCount = 0;
        for (var index = 1; index < snapshot.Length; index++)
        {
            var previous = snapshot[index - 1];
            var current = snapshot[index];
            var tr = TrueRange(previous, current);
            var upMove = current.High.Value - previous.High.Value;
            var downMove = previous.Low.Value - current.Low.Value;
            var plusDm = upMove > downMove && upMove > 0m ? upMove : 0m;
            var minusDm = downMove > upMove && downMove > 0m ? downMove : 0m;

            if (index <= Period)
            {
                smoothedTr += tr;
                smoothedPlusDm += plusDm;
                smoothedMinusDm += minusDm;
            }
            else
            {
                smoothedTr = smoothedTr - smoothedTr / Period + tr;
                smoothedPlusDm = smoothedPlusDm - smoothedPlusDm / Period + plusDm;
                smoothedMinusDm = smoothedMinusDm - smoothedMinusDm / Period + minusDm;
            }

            if (index < Period) continue;
            var plusDi = smoothedTr == 0m ? 0m : (smoothedPlusDm / smoothedTr) * 100m;
            var minusDi = smoothedTr == 0m ? 0m : (smoothedMinusDm / smoothedTr) * 100m;
            var denominator = plusDi + minusDi;
            var dx = denominator == 0m ? 0m : (Math.Abs(plusDi - minusDi) / denominator) * 100m;
            plusPoints.Add(new IndicatorPoint(current.CloseTime, plusDi));
            minusPoints.Add(new IndicatorPoint(current.CloseTime, minusDi));

            if (dxSeedCount < Period)
            {
                dxSeedSum += dx;
                dxSeedCount++;
                if (dxSeedCount < Period) continue;
                adx = dxSeedSum / Period;
            }
            else
            {
                adx = (adx * (Period - 1m) + dx) / Period;
            }

            adxPoints.Add(new IndicatorPoint(current.CloseTime, adx));
        }

        return Result(plusPoints, minusPoints, adxPoints);
    }

    private static decimal TrueRange(Candle previous, Candle current) =>
        Math.Max(current.High.Value - current.Low.Value,
            Math.Max(Math.Abs(current.High.Value - previous.Close.Value),
                Math.Abs(current.Low.Value - previous.Close.Value)));

    private static MultiSeriesIndicatorResult Result(IReadOnlyCollection<IndicatorPoint> plus,
        IReadOnlyCollection<IndicatorPoint> minus, IReadOnlyCollection<IndicatorPoint> adx) => new([
            new NamedIndicatorSeries("+DI", new IndicatorSeries(plus)),
            new NamedIndicatorSeries("-DI", new IndicatorSeries(minus)),
            new NamedIndicatorSeries("ADX", new IndicatorSeries(adx))]);

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
