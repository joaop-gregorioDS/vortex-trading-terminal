using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Charts;

/// <summary>Builds a chart from the prints the session published for one instrument.</summary>
public static class LiveChartSnapshot
{
    /// <summary>Identifies a chart built from session prints rather than Yahoo bars.</summary>
    public const string SourceId = "paper-session-v1";

    /// <summary>Identifies a chart built from the Yahoo minute bars of the open pregão.</summary>
    public const string YahooSourceId = "yahoo-session-v1";

    private static readonly IndicatorSpecification[] Specifications =
    [
        new SmaSpecification(20),
        new MacdSpecification(12, 26, 9),
        new BollingerBandsSpecification(20, 2.0m),
        new RsiSpecification(14),
        new VwapSpecification()
    ];

    public static bool TryParseInterval(string? token, out CandleInterval interval)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "1m":
                interval = CandleInterval.OneMinute;
                return true;
            case "5m":
                interval = CandleInterval.FiveMinutes;
                return true;
            case "15m":
                interval = CandleInterval.FifteenMinutes;
                return true;
            case "60m":
            case "1h":
                interval = CandleInterval.OneHour;
                return true;
            default:
                interval = CandleInterval.OneMinute;
                return false;
        }
    }

    /// <summary>Aggregates the instrument's prints. Returns null when that book has no print yet.</summary>
    public static HistoricalChartSnapshot? Build(
        PaperTradingSession session,
        AssetMarketProfile profile,
        CandleInterval interval,
        int maxBars)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(interval);

        var bars = Math.Clamp(maxBars, 1, 500);
        var sessionBars = session.BarsFor(profile.InstrumentId);
        var batch = sessionBars.Count > 0
            ? SessionBarSeries.Aggregate(profile.InstrumentId, interval, sessionBars, bars)
            : PrintSeries.Aggregate(
                profile.InstrumentId,
                interval,
                session.ObservationsFor(profile.InstrumentId)
                    .Select(observation => new MarketPrint(observation.EventTime, observation.ReferencePrice, observation.Volume))
                    .ToArray(),
                bars);
        if (batch.Count == 0)
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
                continue;
            }
        }

        var context = new HistoricalChartSourceContext(
            profile.InstrumentId,
            interval,
            sessionBars.Count > 0 ? YahooSourceId : SourceId,
            IsDemo: true);
        return new HistoricalChartSnapshot(context, batch, results.AsReadOnly());
    }
}
