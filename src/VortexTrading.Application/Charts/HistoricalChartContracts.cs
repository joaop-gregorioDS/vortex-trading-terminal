using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Charts;

public sealed record HistoricalChartSourceContext(
    InstrumentId InstrumentId, CandleInterval Interval, string SourceId, bool IsDemo);

public sealed record HistoricalChartRequest(
    InstrumentId InstrumentId, CandleInterval Interval, int MaxBars,
    IReadOnlyList<IndicatorSpecification> Indicators);

public sealed record HistoricalChartIndicator(
    IndicatorSpecification Specification, MultiSeriesIndicatorResult Result);

public sealed record HistoricalChartSnapshot(
    HistoricalChartSourceContext Context, IReadOnlyList<Candle> Candles,
    IReadOnlyList<HistoricalChartIndicator> Indicators);

public enum HistoricalChartFailure
{
    ResourceLimit,
    ContextNotFound,
    InvalidConfiguration,
    CalculationUnavailable,
    SourceUnavailable
}

public sealed class HistoricalChartException(HistoricalChartFailure failure) : Exception("Historical chart request failed.")
{
    public HistoricalChartFailure Failure { get; } = failure;
}
