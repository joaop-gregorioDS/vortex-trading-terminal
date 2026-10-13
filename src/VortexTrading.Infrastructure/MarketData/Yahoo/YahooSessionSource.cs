using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.MarketData.Yahoo;

/// <summary>One synchronized read of the catalog from the free Yahoo chart.</summary>
public interface IYahooSessionSource
{
    Task<MarketRound?> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Loads every catalog symbol together and clips the bars to the B3 cash session of PETR4.
/// A missing reference quote returns null so the tape stays where it was.
/// </summary>
public sealed class YahooSessionSource(IHttpClientFactory httpClientFactory) : IYahooSessionSource
{
    public async Task<MarketRound?> LoadAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        var profiles = CandleAggregator.Catalog;
        var documents = new YahooChartDocument?[profiles.Count];
        var http = httpClientFactory.CreateClient(nameof(YahooFinanceChartClient));
        var charts = new YahooFinanceChartClient(http);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, profiles.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var profile = profiles[index];
                if (!YahooSeriesCatalog.TryForProfile(profile, out var spec))
                {
                    return;
                }

                // Five days, not one: the one-day window often arrives without candles while the
                // last pregão is still in the longer series. Cash hours are kept and the chart
                // aggregates those minutes onto the formation clock.
                var interval = new YahooInterval("1m", "1m", "5d", CandleInterval.OneMinute);

                try
                {
                    documents[index] = await charts.LoadDocumentAsync(
                        spec.Symbol, interval, spec.TickSize, spec.Decimals, YahooFinanceChartClient.SessionSeriesBars,
                        token, spec.Scale);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is YahooChartException or HttpRequestException or TaskCanceledException)
                {
                    documents[index] = null;
                }
            });

        YahooChartDocument? reference = null;
        for (var index = 0; index < profiles.Count; index++)
        {
            if (profiles[index].Ticker.Equals("PETR4", StringComparison.OrdinalIgnoreCase))
            {
                reference = documents[index];
            }
        }

        if (reference is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var open = B3Session.IsOpen(reference.MarketState, reference.RegularStart, reference.RegularEnd, now);
        var updates = new List<InstrumentMarketUpdate>(profiles.Count);
        for (var index = 0; index < profiles.Count; index++)
        {
            var document = documents[index];
            if (document is null)
            {
                continue;
            }

            var mapped = document.Series.Bars
                .Select(bar => new SessionBar(
                    new UtcInstant(bar.OpenTime), bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
                .ToArray();
            var clipped = B3Session.ClipToCashSession(
                mapped, reference.RegularStart, reference.RegularEnd, now);
            if (!TryPrice(open, document, clipped, reference, out var price, out var time))
            {
                continue;
            }

            updates.Add(new InstrumentMarketUpdate(profiles[index].Ticker, price, time, clipped));
        }

        return new MarketRound(open, updates);
    }

    private static bool TryPrice(
        bool open,
        YahooChartDocument document,
        IReadOnlyList<SessionBar> clipped,
        YahooChartDocument reference,
        out decimal price,
        out DateTimeOffset time)
    {
        var quoted = document.RegularMarketTime;
        var quote = document.RegularMarketPrice;
        var inside = open
            && quoted is { } stamp
            && quote is > 0m
            && (reference.RegularStart is not { } start || stamp >= start)
            && (reference.RegularEnd is not { } end || stamp < end);
        if (inside)
        {
            price = quote!.Value;
            time = quoted!.Value;
            return true;
        }

        if (clipped.Count == 0)
        {
            price = 0m;
            time = default;
            return false;
        }

        var last = clipped[^1];
        price = last.Close;
        time = last.OpenTime.Value;
        return price > 0m;
    }
}
