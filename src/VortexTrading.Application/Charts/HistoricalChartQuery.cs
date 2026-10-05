using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Charts;

/// <summary>Calculates a bounded static history, not a progressive replay execution.</summary>
public sealed class HistoricalChartQuery(IMarketDataProvider provider, HistoricalChartSourceContext source)
{
    public const int MaximumBars = 200;
    public const int MaximumIndicators = 2;
    private readonly IndicatorBatchCalculator _calculator = new();

    public async Task<HistoricalChartSnapshot> ExecuteAsync(
        HistoricalChartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaxBars is < 1 or > MaximumBars || request.Indicators is null
            || request.Indicators.Count is < 1 or > MaximumIndicators)
            throw new HistoricalChartException(HistoricalChartFailure.ResourceLimit);

        var specifications = request.Indicators.ToArray();
        foreach (var specification in specifications)
        {
            // Upper ceilings bound work; positive periods and MACD ordering remain engine rules.
            var supportedAndBounded = specification switch
            {
                SmaSpecification s => s.Period <= MaximumBars,
                MacdSpecification s => s.FastPeriod <= MaximumBars && s.SlowPeriod <= MaximumBars
                    && s.SignalPeriod <= MaximumBars,
                _ => false
            };
            if (!supportedAndBounded)
                throw new HistoricalChartException(HistoricalChartFailure.ResourceLimit);
        }

        if (request.InstrumentId != source.InstrumentId || request.Interval != source.Interval)
            throw new HistoricalChartException(HistoricalChartFailure.ContextNotFound);

        var candles = new List<Candle>(request.MaxBars);
        try
        {
            await foreach (var candle in provider.StreamCandlesAsync(
                [request.InstrumentId], request.Interval, cancellationToken).WithCancellation(cancellationToken))
            {
                candles.Add(candle);
                if (candles.Count == request.MaxBars) break;
            }
        }
        catch (IOException)
        {
            throw new HistoricalChartException(HistoricalChartFailure.SourceUnavailable);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (candles.Count == 0)
            throw new HistoricalChartException(HistoricalChartFailure.ContextNotFound);

        var batch = Array.AsReadOnly(candles.ToArray());
        if (batch.Any(candle => candle is null || candle.InstrumentId != request.InstrumentId || candle.Interval != request.Interval))
            throw new ArgumentException("Server history returned an invalid context.", "candles");
        var results = new List<HistoricalChartIndicator>(specifications.Length);
        foreach (var specification in specifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(new HistoricalChartIndicator(specification, _calculator.Calculate(specification, batch)));
            }
            catch (ArgumentException exception) when (exception.ParamName != "candles")
            {
                // Configuration errors are distinct from invalid server-owned candle sequences.
                throw new HistoricalChartException(HistoricalChartFailure.InvalidConfiguration);
            }
            catch (OverflowException)
            {
                throw new HistoricalChartException(HistoricalChartFailure.CalculationUnavailable);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new HistoricalChartSnapshot(source, batch, Array.AsReadOnly(results.ToArray()));
    }
}
