using System.Globalization;
using System.Text.Json;
using VortexTrading.Application.MarketData;
using VortexTrading.Application.Trading;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Trading;

/// <summary>Preview and submit for an order born on the chart. The series close chooses limit or stop.</summary>
public static class ChartOrders
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static async Task<IResult> Preview(HttpContext context, IChartCloseSource closes)
    {
        var request = await context.Request.ReadFromJsonAsync<ChartPreviewRequest>(Options, context.RequestAborted);
        var prepared = await Prepare(request?.Ticker, request?.Side, request?.Price, request?.Quantity, closes,
            context.RequestAborted);
        return Results.Json(new ChartPreviewResponse(
            prepared.Profile.Ticker,
            prepared.Side == OrderSide.Buy ? "buy" : "sell",
            prepared.Type == OrderType.Stop ? "stop" : "limit",
            TradingMapping.Exact(prepared.Price),
            TradingMapping.Exact(prepared.Close.Close),
            TradingMapping.Exact(prepared.Notional)), Options);
    }

    public static async Task<IResult> Submit(OrderRequest request, PaperTradingSession session, IChartCloseSource closes,
        ProtectionSpec? protection, CancellationToken cancellationToken)
    {
        if (!CandleAggregator.TryGetProfile(new InstrumentId(request.InstrumentId), out var profile) || profile is null
            || !session.Trades(profile.InstrumentId))
        {
            throw new UnknownInstrumentException();
        }

        var prepared = await Prepare(profile.Ticker, request.Side, request.LimitPrice, request.Quantity, closes,
            cancellationToken);
        var now = session.Now();
        var quantity = new Quantity(prepared.Quantity);
        var price = new Price(prepared.Price);
        var order = prepared.Type == OrderType.Stop
            ? Order.CreateStop(OrderId.From(Guid.NewGuid()), profile.InstrumentId, prepared.Side, quantity, price,
                TimeInForce.GTC, now)
            : Order.CreateLimit(OrderId.From(Guid.NewGuid()), profile.InstrumentId, prepared.Side, quantity, price,
                TimeInForce.GTC, now, OrderOrigin.Chart);

        await session.SubmitAsync(order, prepared.Close.OpenTime, cancellationToken);
        if (protection is { } spec)
        {
            session.RememberProtection(order, spec);
        }

        return Results.Json(TradingMapping.ToResponse(order, session.ProtectionRole(order.Id)), Options,
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<PreparedChartOrder> Prepare(string? ticker, string? sideText, string? priceText,
        string? quantityText, IChartCloseSource closes, CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(ticker) ? "" : ticker.Trim();
        if (!CandleAggregator.TryGetProfile(requested, out var profile) || profile is null)
        {
            throw new UnknownInstrumentException();
        }

        if (!YahooSeriesCatalog.TryForProfile(profile, out var series))
        {
            throw new TradingTransportException("This instrument has no chart series.");
        }

        var side = sideText switch
        {
            "buy" => OrderSide.Buy,
            "sell" => OrderSide.Sell,
            _ => throw new TradingTransportException("Side must be 'buy' or 'sell'.")
        };
        var rawPrice = TradingMapping.ParseDecimal(priceText, "price");
        var quantity = string.IsNullOrWhiteSpace(quantityText)
            ? 1m
            : TradingMapping.ParseDecimal(quantityText, "quantity");
        if (quantity <= 0m)
        {
            throw new TradingTransportException("The quantity must be positive.");
        }

        var rounded = CandleAggregator.RoundToTick(rawPrice, series.TickSize, series.Decimals);
        if (rounded <= 0m)
        {
            throw new TradingTransportException("The price must be positive.");
        }

        ChartSeriesClose? close;
        try
        {
            close = await closes.LatestMinuteAsync(profile.Ticker, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or YahooChartException
            || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new ChartSeriesUnavailableException();
        }

        if (close is null || close.Close <= 0m)
        {
            throw new ChartSeriesUnavailableException();
        }

        var type = ChartOrderMath.Classify(side, rounded, close.Close);
        var notional = ChartOrderMath.Notional(rounded, quantity, profile.PointValue);
        return new PreparedChartOrder(profile, side, type, rounded, quantity, notional, close);
    }

    private sealed record PreparedChartOrder(
        AssetMarketProfile Profile,
        OrderSide Side,
        OrderType Type,
        decimal Price,
        decimal Quantity,
        decimal Notional,
        ChartSeriesClose Close);
}

public sealed record ChartPreviewRequest
{
    [System.Text.Json.Serialization.JsonRequired] public required string Ticker { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required string Side { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public required string Price { get; init; }
    public string? Quantity { get; init; }
}

public sealed record ChartPreviewResponse(
    string Ticker,
    string Side,
    string Type,
    string Price,
    string ReferenceClose,
    string Notional);

/// <summary>The chart series could not be read. The endpoint turns this into 503.</summary>
public sealed class ChartSeriesUnavailableException : Exception
{
    public ChartSeriesUnavailableException() : base("The chart series is unavailable.")
    {
    }
}
