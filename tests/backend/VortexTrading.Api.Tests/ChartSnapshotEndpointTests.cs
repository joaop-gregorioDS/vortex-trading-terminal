using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VortexTrading.Api.Charts;
using VortexTrading.Application;
using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Application.MarketData;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.MarketData.Replay;

namespace VortexTrading.Api.Tests;

public sealed class ChartSnapshotEndpointTests
{
    private const string Request = """
        {"instrumentId":"11111111-1111-1111-1111-111111111111","interval":"1m","maxBars":200,"indicators":[{"id":"sma","parameters":{"period":20}},{"id":"macd","parameters":{"fastPeriod":12,"slowPeriod":26,"signalPeriod":9}}]}
        """;

    [Fact]
    public async Task RealHttpSnapshotHasExactDemoCountsNamesAndStringTransport()
    {
        await using var host = await Host.Start();
        var response = await host.Post(Request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Specification", text);
        Assert.DoesNotContain("$type", text);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.True(root.GetProperty("context").GetProperty("isDemo").GetBoolean());
        Assert.Equal("development-history-v1", root.GetProperty("context").GetProperty("sourceId").GetString());
        Assert.Equal(200, root.GetProperty("context").GetProperty("barCount").GetInt32());
        var candles = root.GetProperty("candles");
        Assert.Equal(200, candles.GetArrayLength());
        foreach (var candle in candles.EnumerateArray())
        {
            foreach (var field in new[] { "open", "high", "low", "close", "volume" })
                Assert.Equal(JsonValueKind.String, candle.GetProperty(field).ValueKind);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00\.0000000Z$", candle.GetProperty("closeTime").GetString()!);
        }
        var indicators = root.GetProperty("indicators");
        var sma = indicators[0];
        Assert.Equal("sma", sma.GetProperty("id").GetString());
        Assert.Equal(20, sma.GetProperty("parameters").GetProperty("period").GetInt32());
        Assert.Equal("Value", sma.GetProperty("series")[0].GetProperty("name").GetString());
        Assert.Equal(181, sma.GetProperty("series")[0].GetProperty("points").GetArrayLength());
        var macd = indicators[1].GetProperty("series");
        Assert.Equal(new[] { "MACD", "Signal", "Histogram" }, macd.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(new[] { 175, 167, 167 }, macd.EnumerateArray().Select(s => s.GetProperty("points").GetArrayLength()));
        foreach (var indicator in indicators.EnumerateArray())
            foreach (var series in indicator.GetProperty("series").EnumerateArray())
                foreach (var point in series.GetProperty("points").EnumerateArray())
                    Assert.Equal(JsonValueKind.String, point.GetProperty("value").ValueKind);
        Assert.Equal(candles[19].GetProperty("closeTime").GetString(), sma.GetProperty("series")[0].GetProperty("points")[0].GetProperty("time").GetString());
        Assert.Equal(candles[25].GetProperty("closeTime").GetString(), macd[0].GetProperty("points")[0].GetProperty("time").GetString());
        Assert.Equal(candles[33].GetProperty("closeTime").GetString(), macd[1].GetProperty("points")[0].GetProperty("time").GetString());
    }

    [Fact]
    public async Task DefaultsAndWarmUpRemainValidWithoutPadding()
    {
        await using var host = await Host.Start();
        using var full = JsonDocument.Parse(await (await host.Post(Request.Replace("\"maxBars\":200,", ""))).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(200, full.RootElement.GetProperty("candles").GetArrayLength());
        var response = await host.Post(Request.Replace("\"maxBars\":200", "\"maxBars\":1"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var shortResult = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, shortResult.RootElement.GetProperty("candles").GetArrayLength());
        foreach (var indicator in shortResult.RootElement.GetProperty("indicators").EnumerateArray())
            foreach (var series in indicator.GetProperty("series").EnumerateArray())
                Assert.Equal(0, series.GetProperty("points").GetArrayLength());
    }

    [Theory]
    [InlineData("unknown id")]
    [InlineData("missing period")]
    [InlineData("wrong parameter shape")]
    [InlineData("null parameters")]
    [InlineData("null indicator")]
    [InlineData("invalid guid")]
    [InlineData("empty guid")]
    [InlineData("unknown interval")]
    [InlineData("fractional period")]
    [InlineData("unrepresentable integer")]
    [InlineData("malformed json")]
    public async Task TransportErrorsReturnSanitized400(string kind)
    {
        var request = kind switch
        {
            "unknown id" => Request.Replace("\"sma\"", "\"ema\""),
            "missing period" => Request.Replace("\"period\":20", ""),
            "wrong parameter shape" => Request.Replace("\"period\":20", "\"fastPeriod\":20"),
            "null parameters" => Request.Replace("{\"period\":20}", "null"),
            "null indicator" => Request.Replace("{\"id\":\"sma\",\"parameters\":{\"period\":20}}", "null"),
            "invalid guid" => Request.Replace("11111111-1111-1111-1111-111111111111", "not-a-guid"),
            "empty guid" => Request.Replace("11111111-1111-1111-1111-111111111111", Guid.Empty.ToString()),
            "unknown interval" => Request.Replace("\"1m\"", "\"free-form\""),
            "fractional period" => Request.Replace("\"period\":20", "\"period\":1.5"),
            "unrepresentable integer" => Request.Replace("\"period\":20", "\"period\":2147483648"),
            _ => "{"
        };
        await using var host = await Host.Start();
        await AssertProblem(await host.Post(request), HttpStatusCode.BadRequest, "invalid_request");
    }

    [Theory]
    [InlineData("bars zero")]
    [InlineData("bars large")]
    [InlineData("period ceiling")]
    [InlineData("macd ceiling")]
    [InlineData("too many indicators")]
    [InlineData("empty indicators")]
    public async Task ResourceBoundsReturn400WithoutMathematicalRuleDuplication(string kind)
    {
        var request = kind switch
        {
            "bars zero" => Request.Replace("\"maxBars\":200", "\"maxBars\":0"),
            "bars large" => Request.Replace("\"maxBars\":200", "\"maxBars\":201"),
            "period ceiling" => Request.Replace("\"period\":20", "\"period\":201"),
            "macd ceiling" => Request.Replace("\"signalPeriod\":9", "\"signalPeriod\":201"),
            "too many indicators" => Request.Replace("\"indicators\":[", "\"indicators\":[{\"id\":\"sma\",\"parameters\":{\"period\":1}},"),
            _ => Request[..Request.IndexOf("\"indicators\":", StringComparison.Ordinal)] + "\"indicators\":[]}"
        };
        await using var host = await Host.Start();
        await AssertProblem(await host.Post(request), HttpStatusCode.BadRequest, "resource_limit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownButUnsupportedContextReturns404(bool interval)
    {
        await using var host = await Host.Start();
        var request = interval ? Request.Replace("\"1m\"", "\"5m\"")
            : Request.Replace("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222");
        await AssertProblem(await host.Post(request), HttpStatusCode.NotFound, "context_not_found");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MathematicalErrorsAre422FromTheFrozenConstructors(bool macd)
    {
        await using var host = await Host.Start();
        var request = macd ? Request.Replace("\"fastPeriod\":12", "\"fastPeriod\":26") : Request.Replace("\"period\":20", "\"period\":0");
        await AssertProblem(await host.Post(request), HttpStatusCode.UnprocessableEntity, "invalid_configuration");
    }

    [Fact]
    public async Task OversizedBodyIs413()
    {
        await using var host = await Host.Start();
        await AssertProblem(await host.Post(Request + new string(' ', ChartSnapshotEndpoint.MaximumBodyBytes)),
            HttpStatusCode.RequestEntityTooLarge, "body_too_large");
    }

    [Fact]
    public async Task ChunkedOversizedBodyWithoutContentLengthIsAlsoBounded()
    {
        await using var host = await Host.Start();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(Request + new string(' ', ChartSnapshotEndpoint.MaximumBodyBytes)));
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/chart-snapshots") { Content = new StreamContent(body) };
        message.Headers.TransferEncodingChunked = true;
        using var response = await host.Send(message);
        await AssertProblem(response, HttpStatusCode.RequestEntityTooLarge, "body_too_large");
    }

    [Fact]
    public async Task ProviderUnavailableIs503WithASafeErrorCode()
    {
        await using var host = await Host.Start(new UnavailableProvider());
        await AssertProblem(await host.Post(Request), HttpStatusCode.ServiceUnavailable, "source_unavailable");
    }

    [Fact]
    public async Task InvalidInternalDatasetIs500AndNeverClientError()
    {
        var first = DevelopmentCandleDataset.Create().Events[0];
        await using var host = await Host.Start(new HistoricalReplayProvider(new HistoricalReplayDataset([first, first])));
        await AssertProblem(await host.Post(Request), HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task OverflowFromValidDataAndConfigurationIs422()
    {
        var price = new Price(decimal.MaxValue);
        var events = DevelopmentCandleDataset.Create().Events.Take(20).Select(e => ReplayEvent.FromCandle(
            new Candle(e.Candle!.InstrumentId, e.Candle.Interval, e.Candle.OpenTime, price, price, price, price, e.Candle.Volume))).ToArray();
        await using var host = await Host.Start(new HistoricalReplayProvider(new HistoricalReplayDataset(events)));
        await AssertProblem(await host.Post(Request), HttpStatusCode.UnprocessableEntity, "calculation_unavailable");
    }

    [Fact]
    public async Task ProductionDoesNotMapOrRegisterDemoChart()
    {
        await using var host = await Host.Start(environment: Environments.Production);
        var response = await host.Post(Request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(host.App.Services.GetService<IMarketDataProvider>());
    }

    [Fact]
    public void MappingPreservesFullDecimalAndTickPrecisionAcrossCultures()
    {
        var time = new UtcInstant(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567));
        var value = 10.000000000000000000000000001m;
        var price = new Price(value);
        var candle = new Candle(DevelopmentCandleDataset.InstrumentId, CandleInterval.OneMinute, time, price, price, price, price, new Quantity(value));
        var specification = new SmaSpecification(1);
        var result = new MultiSeriesIndicatorResult([new NamedIndicatorSeries("Value", new IndicatorSeries([new IndicatorPoint(candle.CloseTime, value)]))]);
        var context = new HistoricalChartSourceContext(candle.InstrumentId, candle.Interval, "precision-fixture", true);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var mapped = ChartSnapshotMapping.ToResponse(new HistoricalChartSnapshot(context, [candle], [new(specification, result)]));
            var text = JsonSerializer.Serialize(mapped, ChartSnapshotEndpoint.JsonOptions);
            using var json = JsonDocument.Parse(text);
            Assert.Equal("10.000000000000000000000000001", mapped.Candles[0].Close);
            Assert.Equal(mapped.Candles[0].Close, mapped.Indicators[0].Series[0].Points[0].Value);
            Assert.Equal("2026-10-01T10:00:00.1234567Z", mapped.Candles[0].OpenTime);
            Assert.Equal("2026-10-01T10:01:00.1234567Z", mapped.Indicators[0].Series[0].Points[0].Time);
            Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("candles")[0].GetProperty("volume").ValueKind);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String);
        if (code is not null) Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        foreach (var forbidden in new[] { "Exception", "Specification", "VortexTrading", "candles", "private", "K:\\" })
            Assert.DoesNotContain(forbidden, body);
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App => app;
        public Task<HttpResponseMessage> Post(string body) => client.PostAsync("/api/chart-snapshots", new StringContent(body, Encoding.UTF8, "application/json"));
        public Task<HttpResponseMessage> Send(HttpRequestMessage message) => client.SendAsync(message);
        public static async Task<Host> Start(IMarketDataProvider? provider = null, string environment = "Development")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddApplication();
            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddDevelopmentHistoricalCharts();
                if (provider is not null) builder.Services.AddSingleton(provider);
            }
            builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Detail = null;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            });
            var app = builder.Build();
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            if (app.Environment.IsDevelopment()) app.MapHistoricalChartSnapshots();
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

    private sealed class UnavailableProvider : IMarketDataProvider
    {
        public MarketDataProviderDescriptor Descriptor { get; } = new("unavailable", "Unavailable", MarketDataCapabilities.Candles);
        public IAsyncEnumerable<Quote> StreamQuotesAsync(IReadOnlyCollection<InstrumentId> ids, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<Trade> StreamTradesAsync(IReadOnlyCollection<InstrumentId> ids, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<Candle> StreamCandlesAsync(IReadOnlyCollection<InstrumentId> ids, CandleInterval interval, CancellationToken token)
            => throw new IOException("private source details");
    }
}
