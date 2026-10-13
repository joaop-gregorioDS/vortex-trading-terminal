using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VortexTrading.Api.Market;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Tests;

public sealed class YahooHistoryEndpointTests
{
    private const string Payload = """
        {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA"},"timestamp":[1791205200],"indicators":{"quote":[{"open":[53.15999984741211],"high":[56.75],"low":[53.0],"close":[55.36000061035156],"volume":[100]}]}}],"error":null}}
        """;

    [Fact]
    public async Task AnEquityHistoryUsesTheYahooSymbolAndDoesNotRequireTheSession()
    {
        await using var host = await Host.Start(Payload);
        var response = await host.Client.GetAsync("/api/market/history?ticker=PETR4&interval=1d&maxBars=30", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal("PETR4", root.GetProperty("ticker").GetString());
        Assert.Equal("PETR4.SA", root.GetProperty("yahooSymbol").GetString());
        Assert.Equal("PETR4", root.GetProperty("seriesName").GetString());
        Assert.Equal("1d", root.GetProperty("interval").GetString());
        Assert.True(root.GetProperty("delayed").GetBoolean());
        var candle = Assert.Single(root.GetProperty("candles").EnumerateArray());
        Assert.Equal("53.16", candle.GetProperty("open").GetString());
        Assert.Equal("55.36", candle.GetProperty("close").GetString());
        Assert.EndsWith("Z", candle.GetProperty("openTime").GetString());
        Assert.Contains("PETR4.SA", host.Paths.Single());
    }

    [Fact]
    public async Task TheMiniIndexUsesTheIbovespaOnTheSamePointScale()
    {
        const string payload = """
            {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"^BVSP"},"timestamp":[1791550800],"indicators":{"quote":[{"open":[209017.25],"high":[209714],"low":[206238],"close":[209066.90625],"volume":[100]}]}}],"error":null}}
            """;
        await using var host = await Host.Start(payload);
        var response = await host.Client.GetAsync("/api/market/history?ticker=WING26&interval=15m", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal("WING26", root.GetProperty("ticker").GetString());
        Assert.Equal("^BVSP", root.GetProperty("yahooSymbol").GetString());
        Assert.Equal("Ibovespa", root.GetProperty("seriesName").GetString());
        Assert.Equal(0, root.GetProperty("decimals").GetInt32());
        Assert.Equal("1", root.GetProperty("tickSize").GetString());
        var candle = Assert.Single(root.GetProperty("candles").EnumerateArray());
        Assert.Equal("209017", candle.GetProperty("open").GetString());
        Assert.Equal("209067", candle.GetProperty("close").GetString());
        Assert.Contains("%5EBVSP", host.Paths.Single(), StringComparison.Ordinal);
        Assert.Contains("interval=15m", host.Paths.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMiniDollarUsesTheCommercialRateInMiniPoints()
    {
        const string payload = """
            {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"BRL=X"},"timestamp":[1791500400],"indicators":{"quote":[{"open":[4.977200031280518],"high":[5.020699977874756],"low":[4.9766998291015625],"close":[5.019199848175049],"volume":[0]}]}}],"error":null}}
            """;
        await using var host = await Host.Start(payload);
        var response = await host.Client.GetAsync("/api/market/history?ticker=WDOU26&interval=1d", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        Assert.Equal("BRL=X", root.GetProperty("yahooSymbol").GetString());
        Assert.Equal("Dólar comercial", root.GetProperty("seriesName").GetString());
        Assert.Equal(1, root.GetProperty("decimals").GetInt32());
        Assert.Equal("0.1", root.GetProperty("tickSize").GetString());
        var candle = Assert.Single(root.GetProperty("candles").EnumerateArray());
        Assert.Equal("4977.2", candle.GetProperty("open").GetString());
        Assert.Equal("5019.2", candle.GetProperty("close").GetString());
        Assert.Contains("BRL%3DX", host.Paths.Single(), StringComparison.Ordinal);
        Assert.Contains("range=1y", host.Paths.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionTapeBuildsTheFormationCandleAndDoesNotCallYahoo()
    {
        await using var host = await Host.Start(Payload, sessionTape: true);
        var response = await host.Client.GetAsync("/api/market/history?ticker=PETR4&interval=15m&maxBars=30", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        var candle = Assert.Single(root.GetProperty("candles").EnumerateArray());
        Assert.Equal("2026-10-13T13:00:00Z", candle.GetProperty("openTime").GetString());
        Assert.Equal("2026-10-13T13:15:00Z", candle.GetProperty("closeTime").GetString());
        Assert.Equal("10.00", candle.GetProperty("open").GetString());
        Assert.Equal("12.00", candle.GetProperty("high").GetString());
        Assert.Equal("9.00", candle.GetProperty("low").GetString());
        Assert.Equal("11.50", candle.GetProperty("close").GetString());
        var minutes = root.GetProperty("formingMinutes").EnumerateArray().ToArray();
        Assert.Equal(2, minutes.Length);
        Assert.Equal("2026-10-13T13:00:00Z", minutes[0].GetProperty("openTime").GetString());
        Assert.Equal("2026-10-13T13:14:00Z", minutes[1].GetProperty("openTime").GetString());
        Assert.Equal("11.50", minutes[1].GetProperty("close").GetString());
        Assert.Empty(host.Paths);
    }

    [Fact]
    public async Task AnUnknownTickerNeverCallsYahoo()
    {
        await using var host = await Host.Start(Payload);
        var response = await host.Client.GetAsync("/api/market/history?ticker=UNKNOWN&interval=1d", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Paths);
    }

    [Fact]
    public async Task AnUnsupportedIntervalIsRejectedBeforeYahoo()
    {
        await using var host = await Host.Start(Payload);
        var response = await host.Client.GetAsync("/api/market/history?ticker=PETR4&interval=2m", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Paths);
    }

    private sealed class Host(WebApplication app, HttpClient client, List<string> paths) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public List<string> Paths => paths;

        public static async Task<Host> Start(string payload, bool sessionTape = false)
        {
            var paths = new List<string>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var http = new HttpClient(new RecordingHandler(payload, paths))
            {
                BaseAddress = new Uri("https://query1.finance.yahoo.com/")
            };
            builder.Services.AddSingleton(new YahooFinanceChartClient(http));
            if (sessionTape)
            {
                var session = new PaperTradingSession();
                var open = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
                session.ApplyMarketRound(new MarketRound(false,
                [
                    new InstrumentMarketUpdate("PETR4", 11.5m, open.AddMinutes(14),
                    [
                        new SessionBar(new UtcInstant(open), 10m, 11m, 9m, 10.5m, 3m),
                        new SessionBar(new UtcInstant(open.AddMinutes(14)), 11m, 12m, 10.5m, 11.5m, 2m)
                    ])
                ]));
                builder.Services.AddSingleton(session);
            }

            var app = builder.Build();
            app.MapYahooHistoryEndpoints();
            await app.StartAsync();
            return new Host(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, paths);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class RecordingHandler(string body, List<string> paths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            paths.Add(request.RequestUri?.PathAndQuery ?? "");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
