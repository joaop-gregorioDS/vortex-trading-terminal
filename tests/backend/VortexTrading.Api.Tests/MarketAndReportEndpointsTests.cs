using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VortexTrading.Api.Market;
using VortexTrading.Api.Reports;
using VortexTrading.Api.Trading;
using VortexTrading.Application;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Tests;

public sealed class MarketAndReportEndpointsTests
{
    [Fact]
    public async Task MarketQuotesReturns15Instruments()
    {
        await using var host = await Host.Start();
        var response = await host.Client.GetAsync("/api/market/quotes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var array = json.RootElement;
        Assert.Equal(15, array.GetArrayLength());

        var tickers = array.EnumerateArray().Select(e => e.GetProperty("ticker").GetString()).ToArray();
        Assert.Contains("PETR4", tickers);
        Assert.Contains("WING26", tickers);
        Assert.Contains("WDOU26", tickers);
        Assert.Contains("VALE3", tickers);
        Assert.Contains("ITUB4", tickers);
    }

    [Fact]
    public async Task PerformanceReportIsDerivedFromClosedFills()
    {
        await using var host = await Host.Start();
        var empty = await host.Client.GetAsync("/api/reports/performance", TestContext.Current.CancellationToken);
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("100000", emptyJson.RootElement.GetProperty("startingBalance").GetString());
        Assert.Equal(0, emptyJson.RootElement.GetProperty("totalTrades").GetInt32());
        Assert.Equal("0", emptyJson.RootElement.GetProperty("realizedPnl").GetString());

        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        await RoundTrip(session, petr.InstrumentId, 10m, 15m);

        var response = await host.Client.GetAsync("/api/reports/performance", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("totalTrades").GetInt32());
        Assert.Equal("5", root.GetProperty("realizedPnl").GetString());
        Assert.Equal("100005", root.GetProperty("currentBalance").GetString());
        Assert.Equal("PETR4", root.GetProperty("bestTrade").GetProperty("ticker").GetString());
        Assert.Equal(1, root.GetProperty("equityCurve").GetArrayLength());
    }

    [Fact]
    public async Task ClosedTradesComeFromFillsAndFilterByTicker()
    {
        await using var host = await Host.Start();
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        await RoundTrip(session, petr.InstrumentId, 10m, 12m);

        var response = await host.Client.GetAsync("/api/reports/trades", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var trade = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("PETR4", trade.GetProperty("ticker").GetString());
        Assert.Equal("2", trade.GetProperty("pnl").GetString());

        var other = await host.Client.GetAsync("/api/reports/trades?ticker=VALE3", TestContext.Current.CancellationToken);
        using var otherJson = JsonDocument.Parse(await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(otherJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task EmergencyFlatSubmitsAClosingOrderForEveryOpenPosition()
    {
        await using var host = await Host.Start();
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        var vale = CandleAggregator.GetProfile("VALE3");
        await OpenLong(session, petr.InstrumentId, 10m);
        await OpenLong(session, vale.InstrumentId, 70m);

        var response = await host.Client.PostAsync("/api/orders/emergency-flat", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, json.RootElement.GetProperty("flattenedPositions").GetInt32());

        var later = session.Now().Value.AddMinutes(1);
        session.Publish(petr.InstrumentId, new UtcInstant(later), 10m, 10m);
        session.Publish(vale.InstrumentId, new UtcInstant(later.AddMinutes(1)), 70m, 10m);

        var portfolio = session.Portfolio();
        Assert.Equal(0m, portfolio.Positions[petr.InstrumentId].Quantity);
        Assert.Equal(0m, portfolio.Positions[vale.InstrumentId].Quantity);
    }

    /// <summary>
    /// A market order needs a known reference price, and it fills only on a print strictly later than submission.
    /// </summary>
    private static async Task OpenLong(PaperTradingSession session, Domain.Markets.InstrumentId instrumentId, decimal price)
    {
        session.Publish(instrumentId, session.Now(), price, 10m);
        await session.SubmitAsync(Order.CreateMarket(OrderId.From(Guid.NewGuid()), instrumentId, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, session.Now()), TestContext.Current.CancellationToken);
        session.Publish(instrumentId, new UtcInstant(session.Now().Value.AddMinutes(1)), price, 10m);
    }

    private static async Task RoundTrip(PaperTradingSession session, Domain.Markets.InstrumentId instrumentId,
        decimal entry, decimal exit)
    {
        await OpenLong(session, instrumentId, entry);
        await session.SubmitAsync(Order.CreateMarket(OrderId.From(Guid.NewGuid()), instrumentId, OrderSide.Sell,
            new Quantity(1m), TimeInForce.GTC, session.Now()), TestContext.Current.CancellationToken);
        session.Publish(instrumentId, new UtcInstant(session.Now().Value.AddMinutes(1)), exit, 10m);
    }

    [Fact]
    public async Task TerminalSnapshotReadsQuotesPortfolioOrdersAndTheDaySummary()
    {
        await using var host = await Host.Start();
        var empty = await host.Client.GetAsync("/api/terminal", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(15, emptyJson.RootElement.GetProperty("quotes").GetArrayLength());
        Assert.Equal("100000", emptyJson.RootElement.GetProperty("portfolio").GetProperty("cash").GetString());
        Assert.Equal(0, emptyJson.RootElement.GetProperty("orders").GetArrayLength());
        Assert.Equal(0, emptyJson.RootElement.GetProperty("performance").GetProperty("totalTrades").GetInt32());
        Assert.Equal("100000", emptyJson.RootElement.GetProperty("performance").GetProperty("currentBalance").GetString());

        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        await RoundTrip(session, petr.InstrumentId, 10m, 15m);

        var response = await host.Client.GetAsync("/api/terminal", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("performance").GetProperty("totalTrades").GetInt32());
        Assert.Equal("5", root.GetProperty("performance").GetProperty("realizedPnl").GetString());
        Assert.Equal(2, root.GetProperty("orders").GetArrayLength());
        var quote = root.GetProperty("quotes").EnumerateArray().Single(item => item.GetProperty("ticker").GetString() == "PETR4");
        Assert.Equal("15.00", quote.GetProperty("lastPrice").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("simulatedTime").GetString()));
    }

    [Fact]
    public async Task SessionResetErasesTheRoundTripAndAcceptsTheOpeningPrint()
    {
        await using var host = await Host.Start();
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        await RoundTrip(session, petr.InstrumentId, 10m, 15m);

        var before = await host.Client.GetAsync("/api/terminal", TestContext.Current.CancellationToken);
        using var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, beforeJson.RootElement.GetProperty("performance").GetProperty("totalTrades").GetInt32());

        var reset = await host.Client.PostAsync("/api/session/reset", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var resetJson = JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ok", resetJson.RootElement.GetProperty("status").GetString());

        var after = await host.Client.GetAsync("/api/terminal", TestContext.Current.CancellationToken);
        using var afterJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, afterJson.RootElement.GetProperty("orders").GetArrayLength());
        Assert.Equal(0, afterJson.RootElement.GetProperty("performance").GetProperty("totalTrades").GetInt32());
        Assert.Equal("100000", afterJson.RootElement.GetProperty("performance").GetProperty("currentBalance").GetString());

        session.PublishFeedRound(TimeSpan.FromSeconds(15));
        var quotes = await host.Client.GetAsync("/api/market/quotes", TestContext.Current.CancellationToken);
        using var quotesJson = JsonDocument.Parse(await quotes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var quote = quotesJson.RootElement.EnumerateArray().Single(item => item.GetProperty("ticker").GetString() == "PETR4");
        Assert.False(string.IsNullOrEmpty(quote.GetProperty("lastPrice").GetString()));
    }

    [Fact]
    public async Task EmergencyFlatCancelsAndFlattensSafely()
    {
        await using var host = await Host.Start();
        var response = await host.Client.PostAsync("/api/orders/emergency-flat", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ChartEndpointAggregatesThePrintsOfTheRequestedInstrument()
    {
        await using var host = await Host.Start();
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");
        var vale = CandleAggregator.GetProfile("VALE3");
        var first = new UtcInstant(new DateTimeOffset(2026, 10, 2, 14, 0, 10, TimeSpan.Zero));
        session.Publish(petr.InstrumentId, first, 10m, 5m);
        session.Publish(petr.InstrumentId, new UtcInstant(first.Value.AddSeconds(20)), 12m, 7m);
        session.Publish(vale.InstrumentId, new UtcInstant(first.Value.AddSeconds(30)), 70m, 3m);

        var empty = await host.Client.GetAsync("/api/chart?ticker=ITUB4&interval=1m", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("empty", emptyJson.RootElement.GetProperty("status").GetString());

        var response = await host.Client.GetAsync("/api/chart?ticker=PETR4&interval=1m", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var candle = Assert.Single(json.RootElement.GetProperty("candles").EnumerateArray());
        Assert.Equal("10", candle.GetProperty("open").GetString());
        Assert.Equal("12", candle.GetProperty("high").GetString());
        Assert.Equal("10", candle.GetProperty("low").GetString());
        Assert.Equal("12", candle.GetProperty("close").GetString());
        Assert.Equal("12", candle.GetProperty("volume").GetString());

        var valeResponse = await host.Client.GetAsync("/api/chart?ticker=VALE3&interval=1m", TestContext.Current.CancellationToken);
        using var valeJson = JsonDocument.Parse(await valeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var valeCandle = Assert.Single(valeJson.RootElement.GetProperty("candles").EnumerateArray());
        Assert.Equal("70", valeCandle.GetProperty("close").GetString());

        var unknown = await host.Client.GetAsync("/api/chart?ticker=UNKNOWN", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task MarketDepthReportsThePrintAndTheOperatorsRestingOrder()
    {
        await using var host = await Host.Start();
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var profile = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        session.Publish(profile.InstrumentId, new UtcInstant(created.Value.AddMinutes(1)), 10m, 100m);
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), profile.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(1m), TimeInForce.GTC, created);
        await session.SubmitAsync(order, TestContext.Current.CancellationToken);

        var response = await host.Client.GetAsync("/api/market/depth?ticker=PETR4", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;

        Assert.Equal("PETR4", root.GetProperty("ticker").GetString());
        Assert.Equal("10.00", root.GetProperty("lastPrice").GetString());
        Assert.Equal("100", root.GetProperty("availableVolume").GetString());
        Assert.Equal("0.01", root.GetProperty("tickSize").GetString());
        var resting = Assert.Single(root.GetProperty("restingOrders").EnumerateArray());
        Assert.Equal("buy", resting.GetProperty("side").GetString());
        Assert.Equal("1.00", resting.GetProperty("limitPrice").GetString());

        var unknown = await host.Client.GetAsync("/api/market/depth?ticker=UNKNOWN", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public IServiceProvider Services => app.Services;

        public static async Task<Host> Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddApplication();
            builder.Services.AddDevelopmentHistoricalCharts();
            builder.Services.AddSingleton<PaperTradingSession>();

            var app = builder.Build();
            app.MapTradingEndpoints();
            app.MapMarketQuotesEndpoints();
            app.MapReportEndpoints();
            app.MapTerminalEndpoints();

            await app.StartAsync();
            return new Host(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) });
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
