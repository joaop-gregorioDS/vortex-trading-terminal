using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Market;

public sealed record YahooCandleResponse(
    string OpenTime,
    string CloseTime,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume);

public sealed record YahooHistoryResponse(
    string Ticker,
    string YahooSymbol,
    string Interval,
    string Currency,
    bool Delayed,
    int Decimals,
    string TickSize,
    string SeriesName,
    string Note,
    YahooCandleResponse[] Candles,
    YahooCandleResponse[] FormingMinutes);

/// <summary>
/// Yahoo history for the selected catalog asset. Mini contracts are not listed, so the
/// index chart uses the Ibovespa and the dollar chart uses the commercial rate in mini points.
/// The same series feeds the paper session while the B3 cash session is open.
/// </summary>
public static class YahooHistoryEndpoints
{
    private const string EquityNote =
        "Série gratuita do Yahoo Finance. Ela alimenta o pregão enquanto a sessão regular da B3 está aberta e para quando o pregão encerra. Uma ordem do gráfico espera um fechamento posterior desta série.";

    private const string IndexNote =
        "Ibovespa (^BVSP). O Yahoo não lista o índice futuro. Entra no pregão como proxy e para junto com a sessão regular da B3. Uma ordem do gráfico espera um fechamento posterior desta série.";

    private const string DollarNote =
        "Dólar comercial em pontos do mini (BRL=X × 1.000). O Yahoo não lista o dólar futuro. Entra no pregão como proxy e para junto com a sessão regular da B3. Uma ordem do gráfico espera um fechamento posterior desta série.";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void MapYahooHistoryEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/market/history", async (
            HttpContext http,
            YahooFinanceChartClient yahoo,
            string? ticker,
            string? interval,
            int? maxBars,
            CancellationToken cancellationToken) =>
        {
            var requested = string.IsNullOrWhiteSpace(ticker) ? "" : ticker.Trim();
            if (!CandleAggregator.TryGetProfile(requested, out var profile) || profile is null)
            {
                return Problem(StatusCodes.Status404NotFound, "instrument_not_found",
                    "Este ativo não está no pregão.");
            }

            if (!YahooSeriesCatalog.TryForProfile(profile, out var spec))
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "yahoo_symbol_unavailable",
                    "O Yahoo Finance não lista este contrato.");
            }

            var map = new YahooSeries(spec.Symbol, spec.TickSize, spec.Decimals, spec.Scale,
                spec.SeriesName, NoteFor(profile));

            if (!YahooInterval.TryParse(interval, out var window))
            {
                return Problem(StatusCodes.Status400BadRequest, "interval_not_supported",
                    "O intervalo precisa ser 1d, 1h, 15m, 5m ou 1m.");
            }

            var bars = maxBars is null or < 1 ? 180 : Math.Min(maxBars.Value, YahooFinanceChartClient.MaximumBars);
            var session = http.RequestServices.GetService<PaperTradingSession>();
            if (session is not null)
            {
                var minutes = session.BarsFor(profile.InstrumentId);
                if (minutes.Count > 0)
                {
                    var aggregated = SessionBarSeries.Aggregate(
                        profile.InstrumentId, window.CandleInterval, minutes, bars);
                    if (aggregated.Count > 0)
                    {
                        var candles = aggregated.Select(candle => ToCandle(candle.OpenTime.Value, candle.CloseTime.Value,
                            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value,
                            candle.Volume.Value, map.Decimals)).ToArray();
                        var lastOpen = aggregated[^1].OpenTime.Value;
                        var forming = minutes
                            .Where(bar => SessionBarSeries.FormationOpen(bar.OpenTime.Value, window.CandleInterval) == lastOpen)
                            .Select(bar => ToCandle(
                                bar.OpenTime.Value,
                                bar.OpenTime.Value.AddMinutes(1),
                                bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, map.Decimals))
                            .ToArray();
                        return Results.Json(new YahooHistoryResponse(
                            profile.Ticker,
                            map.Symbol,
                            window.Token,
                            "BRL",
                            Delayed: true,
                            map.Decimals,
                            map.TickSize.ToString(CultureInfo.InvariantCulture),
                            map.SeriesName,
                            map.Note,
                            candles,
                            forming), Options);
                    }
                }
            }

            try
            {
                var series = await yahoo.LoadAsync(
                    map.Symbol, window, map.TickSize, map.Decimals, bars, cancellationToken, map.Scale);
                var candles = series.Bars.Select(bar =>
                {
                    var close = new DateTimeOffset(bar.OpenTime.UtcDateTime.Add(window.CandleInterval.Duration), TimeSpan.Zero);
                    return new YahooCandleResponse(
                        YahooChartParser.FormatInstant(bar.OpenTime),
                        YahooChartParser.FormatInstant(close),
                        Money(bar.Open, map.Decimals),
                        Money(bar.High, map.Decimals),
                        Money(bar.Low, map.Decimals),
                        Money(bar.Close, map.Decimals),
                        bar.Volume.ToString(CultureInfo.InvariantCulture));
                }).ToArray();

                return Results.Json(new YahooHistoryResponse(
                    profile.Ticker,
                    series.Symbol,
                    window.Token,
                    series.Currency,
                    Delayed: true,
                    map.Decimals,
                    map.TickSize.ToString(CultureInfo.InvariantCulture),
                    map.SeriesName,
                    map.Note,
                    candles,
                    []), Options);
            }
            catch (YahooChartException)
            {
                return Problem(StatusCodes.Status503ServiceUnavailable, "yahoo_unavailable",
                    "O Yahoo Finance não respondeu com uma série.");
            }
            catch (HttpRequestException)
            {
                return Problem(StatusCodes.Status503ServiceUnavailable, "yahoo_unavailable",
                    "O Yahoo Finance não respondeu com uma série.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Problem(StatusCodes.Status503ServiceUnavailable, "yahoo_unavailable",
                    "O Yahoo Finance não respondeu com uma série.");
            }
        });
    }

    private static string NoteFor(AssetMarketProfile profile)
    {
        if (profile.Ticker.Equals("WING26", StringComparison.OrdinalIgnoreCase))
        {
            return IndexNote;
        }

        if (profile.Ticker.Equals("WDOU26", StringComparison.OrdinalIgnoreCase))
        {
            return DollarNote;
        }

        return EquityNote;
    }

    private readonly record struct YahooSeries(
        string Symbol,
        decimal TickSize,
        int Decimals,
        decimal Scale,
        string SeriesName,
        string Note);

    private static YahooCandleResponse ToCandle(
        DateTimeOffset open,
        DateTimeOffset close,
        decimal openPrice,
        decimal high,
        decimal low,
        decimal closePrice,
        decimal volume,
        int decimals) =>
        new(
            YahooChartParser.FormatInstant(open),
            YahooChartParser.FormatInstant(close),
            Money(openPrice, decimals),
            Money(high, decimals),
            Money(low, decimals),
            Money(closePrice, decimals),
            volume.ToString(CultureInfo.InvariantCulture));

    private static string Money(decimal value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
