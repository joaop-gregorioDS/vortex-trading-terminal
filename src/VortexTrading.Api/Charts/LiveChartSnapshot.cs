using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Charts;

/// <summary>Builds a chart snapshot from the prices the simulated session actually published.</summary>
/// <remarks>
/// The demonstration fixture only exists in Development, so a hosted terminal had a market panel and
/// no chart. This derives the series from the live session instead, using the exact decimals the
/// session traded against (ADR-002), and reuses the same indicator calculator and the same response
/// mapping as the demonstration endpoint so both charts are drawn from one implementation.
/// <para>
/// One observation becomes one candle. The simulated feed advances a fixed amount per tick rather
/// than reporting intra-bar movement, so there is no genuine open/high/low to recover; mapping all
/// four values to the observed price states that limit instead of inventing a range.
/// </para>
/// </remarks>
public static class LiveChartSnapshot
{
    /// <summary>Identifies the source so a client can tell this chart from the demo fixture.</summary>
    public const string SourceId = "paper-session-v1";

    private static readonly IndicatorSpecification[] Specifications =
    [
        new SmaSpecification(20),
        new MacdSpecification(12, 26, 9)
    ];

    /// <summary>Builds a snapshot from observations, or null when there is nothing to plot yet.</summary>
    public static HistoricalChartSnapshot? Build(IReadOnlyList<PriceObservation>? observations, int maxBars)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            return null;
        }

        var batch = observations
            .TakeLast(Math.Max(1, maxBars))
            .Select(observation => ToCandle(observation))
            .ToArray();

        if (batch.Length == 0)
        {
            return null;
        }

        var calculator = new IndicatorBatchCalculator();
        var results = new List<HistoricalChartIndicator>(Specifications.Length);
        foreach (var specification in Specifications)
        {
            try
            {
                results.Add(new HistoricalChartIndicator(specification, calculator.Calculate(specification, batch)));
            }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            {
                // A short series cannot support a warm-up, and an out-of-range value cannot be plotted.
                // Dropping that one indicator keeps the price chart usable instead of failing the page.
                continue;
            }
        }

        var context = new HistoricalChartSourceContext(
            PaperTradingSession.InstrumentId, CandleInterval.OneMinute, SourceId, IsDemo: true);
        return new HistoricalChartSnapshot(context, batch, results.AsReadOnly());
    }

    private static Candle ToCandle(PriceObservation observation)
    {
        var price = new Price(observation.ReferencePrice);
        return new Candle(
            PaperTradingSession.InstrumentId,
            CandleInterval.OneMinute,
            observation.EventTime,
            price,
            price,
            price,
            price,
            new Quantity(observation.Volume));
    }
}
