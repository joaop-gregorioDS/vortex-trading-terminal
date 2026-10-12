using System.Net;
using System.Net.Http.Json;
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
using VortexTrading.Infrastructure;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Tests;

public sealed class ChartTradingEndpointTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PreviewClassifiesRoundsAndDoesNotPersist()
    {
        var closes = new ScriptedCloses();
        closes.ByTicker["WING26"] = Close(209000m, minutes: 0);
        await using var host = await Host.Start(closes);

        var response = await host.Client.PostAsJsonAsync("/api/orders/chart-preview", new
        {
            ticker = "WING26",
            side = "buy",
            price = "209335.5",
            quantity = "2"
        }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = json.RootElement;
        Assert.Equal("WING26", root.GetProperty("ticker").GetString());
        Assert.Equal("buy", root.GetProperty("side").GetString());
        Assert.Equal("stop", root.GetProperty("type").GetString());
        Assert.Equal("209336", root.GetProperty("price").GetString());
        Assert.Equal("209000", root.GetProperty("referenceClose").GetString());
        Assert.Equal("83734.4", root.GetProperty("notional").GetString());

        var orders = await host.Client.GetAsync("/api/orders", Token);
        using var listed = JsonDocument.Parse(await orders.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, listed.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("WDOU26", "4972.25", "5000", "limit", "4972.3", "49723")]
    [InlineData("PETR4", "54.845", "54.85", "limit", "54.85", "54.85")]
    [InlineData("WING26", "209000.4", "209100", "limit", "209000", "41800")]
    public async Task PreviewRoundsToTheChartSeriesTick(string ticker, string price, string reference,
        string type, string rounded, string notional)
    {
        var closes = new ScriptedCloses();
        closes.ByTicker[ticker] = Close(decimal.Parse(reference, System.Globalization.CultureInfo.InvariantCulture), 0);
        await using var host = await Host.Start(closes);

        var response = await host.Client.PostAsJsonAsync("/api/orders/chart-preview", new
        {
            ticker,
            side = "buy",
            price
        }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(type, json.RootElement.GetProperty("type").GetString());
        Assert.Equal(rounded, json.RootElement.GetProperty("price").GetString());
        Assert.Equal(reference, json.RootElement.GetProperty("referenceClose").GetString());
        Assert.Equal(notional, json.RootElement.GetProperty("notional").GetString());
    }

    [Fact]
    public async Task UnknownTickerAndASilentSeriesFailClosed()
    {
        var closes = new ScriptedCloses();
        await using var host = await Host.Start(closes);

        var unknown = await host.Client.PostAsJsonAsync("/api/orders/chart-preview", new
        {
            ticker = "UNKNOWN",
            side = "buy",
            price = "10"
        }, Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        closes.ByTicker["PETR4"] = null;
        var silent = await host.Client.PostAsJsonAsync("/api/orders/chart-preview", new
        {
            ticker = "PETR4",
            side = "buy",
            price = "10"
        }, Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, silent.StatusCode);

        closes.Error = new YahooChartException("chart refused");
        var refused = await host.Client.PostAsJsonAsync("/api/orders/chart-preview", new
        {
            ticker = "PETR4",
            side = "buy",
            price = "10"
        }, Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Token));
        Assert.Equal("chart_series_unavailable", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SubmitIgnoresTheClientTypeAndTheReferenceBarDoesNotFill()
    {
        var closes = new ScriptedCloses();
        var reference = Close(50m, minutes: 0);
        closes.ByTicker["PETR4"] = reference;
        await using var host = await Host.Start(closes);
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");

        var response = await host.Client.PostAsJsonAsync("/api/orders", new
        {
            instrumentId = petr.InstrumentId.Value,
            side = "buy",
            type = "market",
            quantity = "1",
            limitPrice = "60.4",
            origin = "chart"
        }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("stop", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("chart", json.RootElement.GetProperty("origin").GetString());
        Assert.Equal("60.4", json.RootElement.GetProperty("limitPrice").GetString());
        Assert.Equal("accepted", json.RootElement.GetProperty("status").GetString());

        var terminal = await host.Client.GetAsync("/api/terminal", Token);
        using var snapshot = JsonDocument.Parse(await terminal.Content.ReadAsStringAsync(Token));
        var listed = Assert.Single(snapshot.RootElement.GetProperty("orders").EnumerateArray());
        Assert.Equal("stop", listed.GetProperty("type").GetString());
        Assert.Equal("chart", listed.GetProperty("origin").GetString());

        session.ObserveChart(petr.InstrumentId, reference.OpenTime, 80m, 10m);
        Assert.Equal("accepted", await Status(host));

        session.ObserveChart(petr.InstrumentId, After(reference.OpenTime, 1), 70m, 10m);
        using var filled = JsonDocument.Parse(await (await host.Client.GetAsync("/api/orders", Token)).Content.ReadAsStringAsync(Token));
        var order = Assert.Single(filled.RootElement.EnumerateArray());
        Assert.Equal("filled", order.GetProperty("status").GetString());
        Assert.Equal("70", order.GetProperty("fills")[0].GetProperty("price").GetString());
    }

    [Fact]
    public async Task SessionPrintAndChartCloseStayOnTheirOwnOrders()
    {
        var closes = new ScriptedCloses();
        closes.ByTicker["PETR4"] = Close(50m, 0);
        await using var host = await Host.Start(closes);
        var session = host.Services.GetRequiredService<PaperTradingSession>();
        var petr = CandleAggregator.GetProfile("PETR4");

        var chart = await host.Client.PostAsJsonAsync("/api/orders", new
        {
            instrumentId = petr.InstrumentId.Value,
            side = "buy",
            type = "limit",
            quantity = "1",
            limitPrice = "60",
            origin = "chart"
        }, Token);
        Assert.Equal(HttpStatusCode.Created, chart.StatusCode);

        var ticket = await host.Client.PostAsJsonAsync("/api/orders", new
        {
            instrumentId = petr.InstrumentId.Value,
            side = "buy",
            type = "limit",
            quantity = "3",
            limitPrice = "10"
        }, Token);
        Assert.Equal(HttpStatusCode.Created, ticket.StatusCode);

        var created = session.Now();
        session.Publish(petr.InstrumentId, After(created, 1), 80m, 100m);
        session.ObserveChart(petr.InstrumentId, After(closes.ByTicker["PETR4"]!.OpenTime, 1), 9m, 10m);

        using var json = JsonDocument.Parse(await (await host.Client.GetAsync("/api/orders", Token)).Content.ReadAsStringAsync(Token));
        var orders = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, orders.Length);
        Assert.All(orders, order => Assert.Equal("accepted", order.GetProperty("status").GetString()));
        Assert.Contains(orders, order => order.GetProperty("origin").GetString() == "chart");
        Assert.Contains(orders, order => order.GetProperty("origin").GetString() == "session");

        var depth = await host.Client.GetAsync("/api/market/depth?ticker=PETR4", Token);
        using var book = JsonDocument.Parse(await depth.Content.ReadAsStringAsync(Token));
        var resting = Assert.Single(book.RootElement.GetProperty("restingOrders").EnumerateArray());
        Assert.Equal("limit", resting.GetProperty("type").GetString());
        Assert.Equal("10.00", resting.GetProperty("limitPrice").GetString());
        Assert.Equal("100", book.RootElement.GetProperty("availableVolume").GetString());
    }

    [Fact]
    public async Task ChartSellWithoutALongIsAccepted()
    {
        var closes = new ScriptedCloses();
        closes.ByTicker["PETR4"] = Close(50m, 0);
        await using var host = await Host.Start(closes);
        var petr = CandleAggregator.GetProfile("PETR4");

        var response = await host.Client.PostAsJsonAsync("/api/orders", new
        {
            instrumentId = petr.InstrumentId.Value,
            side = "sell",
            type = "limit",
            quantity = "1",
            limitPrice = "55",
            origin = "chart"
        }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("accepted", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("limit", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("chart", json.RootElement.GetProperty("origin").GetString());
        Assert.Null(json.RootElement.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task UnknownInstrumentOnSubmitIsNotFound()
    {
        var closes = new ScriptedCloses();
        await using var host = await Host.Start(closes);

        var response = await host.Client.PostAsJsonAsync("/api/orders", new
        {
            instrumentId = Guid.NewGuid(),
            side = "buy",
            type = "limit",
            quantity = "1",
            limitPrice = "10",
            origin = "chart"
        }, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(closes.Calls);
    }

    private static async Task<string?> Status(Host host)
    {
        using var json = JsonDocument.Parse(await (await host.Client.GetAsync("/api/orders", Token)).Content.ReadAsStringAsync(Token));
        return Assert.Single(json.RootElement.EnumerateArray()).GetProperty("status").GetString();
    }

    private static ChartSeriesClose Close(decimal price, int minutes) =>
        new(price, new UtcInstant(new DateTimeOffset(2026, 10, 9, 14, minutes, 0, TimeSpan.Zero)), 10m);

    private static UtcInstant After(UtcInstant instant, int minutes) =>
        new(instant.Value.AddMinutes(minutes));

    private sealed class ScriptedCloses : IChartCloseSource
    {
        public Dictionary<string, ChartSeriesClose?> ByTicker { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Error { get; set; }
        public List<string> Calls { get; } = [];

        public Task<ChartSeriesClose?> LatestMinuteAsync(string ticker, CancellationToken cancellationToken)
        {
            Calls.Add(ticker);
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(ByTicker.TryGetValue(ticker, out var close) ? close : null);
        }
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public IServiceProvider Services => app.Services;

        public static async Task<Host> Start(ScriptedCloses closes)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddApplication();
            builder.Services.AddDevelopmentHistoricalCharts();
            builder.Services.AddSingleton<IChartCloseSource>(closes);
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
