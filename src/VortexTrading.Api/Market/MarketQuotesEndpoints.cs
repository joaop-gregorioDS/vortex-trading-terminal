using System.Globalization;
using System.Text.Json;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Market;

public static class MarketQuotesEndpoints
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    private static readonly Dictionary<string, (string Name, string Category)> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABEV3"] = ("Ambev ON", "Ações"),
        ["B3SA3"] = ("B3 ON", "Ações"),
        ["BBAS3"] = ("Banco do Brasil ON", "Ações"),
        ["BBDC4"] = ("Banco Bradesco PN", "Ações"),
        ["GGBR4"] = ("Gerdau PN", "Ações"),
        ["ITUB4"] = ("Itaú Unibanco PN", "Ações"),
        ["MGLU3"] = ("Magazine Luiza ON", "Ações"),
        ["PETR4"] = ("Petrobras PN", "Ações"),
        ["PRIO3"] = ("PetroRio ON", "Ações"),
        ["RENT3"] = ("Localiza ON", "Ações"),
        ["SUZB3"] = ("Suzano ON", "Ações"),
        ["VALE3"] = ("Vale ON", "Ações"),
        ["WDOU26"] = ("Mini-Dólar Futuro", "Futuros"),
        ["WEGE3"] = ("WEG ON", "Ações"),
        ["WING26"] = ("Mini-Índice Futuro", "Futuros")
    };

    public static void MapMarketQuotesEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/market/quotes", (PaperTradingSession session) =>
            Results.Json(BuildQuotes(session), Options));

        app.MapGet("/api/market/depth", (PaperTradingSession session, string? ticker) =>
        {
            var requested = string.IsNullOrWhiteSpace(ticker) ? "PETR4" : ticker.Trim();
            if (!CandleAggregator.TryGetProfile(requested, out var profile) || profile is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "The instrument is not traded by this session.",
                    extensions: new Dictionary<string, object?> { ["code"] = "instrument_not_found" });
            }

            var book = session.ReadBook(profile.InstrumentId);
            var response = new MarketDepthResponse(
                profile.Ticker,
                profile.InstrumentId.ToString(),
                Price(book.LastPrice?.Value, profile.Decimals),
                Decimal(book.AvailableVolume),
                profile.TickSize.ToString(CultureInfo.InvariantCulture),
                book.RestingOrders.Select(order => new RestingOrderResponse(
                    order.Id.ToString(),
                    order.Side == OrderSide.Buy ? "buy" : "sell",
                    order.Type switch
                    {
                        OrderType.Market => "market",
                        OrderType.Stop => "stop",
                        _ => "limit"
                    },
                    order.LimitPrice is null ? null : Price(order.LimitPrice.Value, profile.Decimals),
                    Decimal(order.RemainingQuantity.Value),
                    order.Status == OrderStatus.PartiallyFilled ? "partially_filled" : "accepted")).ToArray());

            return Results.Json(response, Options);
        });
    }

    /// <summary>Quotes for every catalog instrument, from the prints of the simulated day.</summary>
    public static MarketQuoteResponse[] BuildQuotes(PaperTradingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var day = session.CurrentTime is { } now
            ? DateOnly.FromDateTime(now.Value.UtcDateTime)
            : (DateOnly?)null;

        return CandleAggregator.Catalog.Select(profile =>
        {
            var prints = session.ObservationsFor(profile.InstrumentId)
                .Select(observation => new MarketPrint(observation.EventTime, observation.ReferencePrice, observation.Volume))
                .ToArray();
            var quote = day is null ? DayQuote.Empty : PrintSeries.Summarize(prints, day.Value);
            var (name, category) = Names.TryGetValue(profile.Ticker, out var label)
                ? label
                : (profile.Ticker, "Ações");
            return new MarketQuoteResponse(
                profile.Ticker,
                name,
                category,
                profile.InstrumentId.ToString(),
                Price(quote.Last, profile.Decimals),
                Decimal(quote.ChangeRatio),
                Price(quote.Change, profile.Decimals),
                Price(quote.Low, profile.Decimals),
                Price(quote.High, profile.Decimals),
                quote.Last is null ? "" : Decimal(quote.Volume),
                quote.UpdatedAt is null ? "" : quote.UpdatedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }).ToArray();
    }

    private static string Price(decimal? value, int decimals) =>
        value is null ? "" : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static string Decimal(decimal? value) =>
        value is null ? "" : value.Value.ToString(CultureInfo.InvariantCulture);
}
