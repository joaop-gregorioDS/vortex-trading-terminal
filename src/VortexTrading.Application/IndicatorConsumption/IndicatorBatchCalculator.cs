using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.IndicatorConsumption;

/// <summary>Delegates an explicit batch to one supported frozen indicator without acquiring or filtering data.</summary>
public sealed class IndicatorBatchCalculator
{
    public MultiSeriesIndicatorResult Calculate(
        IndicatorSpecification specification, IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return specification switch
        {
            SmaSpecification s => Single(new SimpleMovingAverage(s.Period).Calculate(candles)),
            EmaSpecification s => Single(new ExponentialMovingAverage(s.Period).Calculate(candles)),
            MacdSpecification s => new MovingAverageConvergenceDivergence(
                s.FastPeriod, s.SlowPeriod, s.SignalPeriod).Calculate(candles),
            RsiSpecification s => Single(new RelativeStrengthIndex(s.Period).Calculate(candles)),
            StochasticSpecification s => new StochasticOscillator(
                s.KPeriod, s.KSmoothingPeriod, s.DPeriod).Calculate(candles),
            BollingerBandsSpecification s => new BollingerBands(
                s.Period, s.StandardDeviationMultiplier).Calculate(candles),
            AtrSpecification s => Single(new AverageTrueRange(s.Period).Calculate(candles)),
            VwapSpecification => Single(new VolumeWeightedAveragePrice().Calculate(candles)),
            AdxSpecification s => new AverageDirectionalIndex(s.Period).Calculate(candles),
            ObvSpecification => Single(new OnBalanceVolume().Calculate(candles)),
            _ => throw new NotSupportedException("Indicator specification type is not supported.")
        };
    }

    private static MultiSeriesIndicatorResult Single(IndicatorSeries series) =>
        new([new NamedIndicatorSeries("Value", series)]);
}
