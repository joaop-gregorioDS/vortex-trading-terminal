using System.Globalization;
using System.Net;
using VortexTrading.Infrastructure.MarketData.Yahoo;

namespace VortexTrading.Infrastructure.Tests;

public sealed class YahooSessionSourceTests
{
    [Fact]
    public async Task TheCatalogRoundFollowsThePetr4SessionAndDropsBarsOutsideIt()
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-1);
        var end = now.AddHours(1);
        var inside = new DateTimeOffset(now.AddMinutes(-5).Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero);
        var outside = start.AddHours(-3);
        var quote = now.AddMinutes(-1);

        var handler = new SymbolHandler(symbol => symbol.Contains("PETR4.SA", StringComparison.Ordinal)
            ? Payload("PETR4.SA", "REGULAR", start, end, quote, 36.55m, inside, outside)
            : """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""");
        var source = new YahooSessionSource(new SingleClientFactory(handler));

        var round = await source.LoadAsync(CancellationToken.None);

        Assert.NotNull(round);
        Assert.True(round.SessionOpen);
        var update = Assert.Single(round.Instruments);
        Assert.Equal("PETR4", update.Ticker);
        Assert.Equal(36.55m, update.Price);
        var bar = Assert.Single(update.MinuteBars);
        Assert.Equal(inside, bar.OpenTime.Value);
    }

    [Fact]
    public async Task AClosedReferenceKeepsTheRoundShut()
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-8);
        var end = now.AddHours(-1);
        var inside = start.AddMinutes(30);
        var handler = new SymbolHandler(symbol => symbol.Contains("PETR4.SA", StringComparison.Ordinal)
            ? Payload("PETR4.SA", "CLOSED", start, end, now, 36.55m, inside, inside.AddHours(-5))
            : """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""");
        var source = new YahooSessionSource(new SingleClientFactory(handler));

        var round = await source.LoadAsync(CancellationToken.None);

        Assert.NotNull(round);
        Assert.False(round.SessionOpen);
        var update = Assert.Single(round.Instruments);
        Assert.Equal(36.10m, update.Price);
    }

    [Fact]
    public async Task AMissingReferenceDoesNotInventARound()
    {
        var handler = new SymbolHandler(_ =>
            """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""");
        var source = new YahooSessionSource(new SingleClientFactory(handler));

        var round = await source.LoadAsync(CancellationToken.None);

        Assert.Null(round);
    }

    private static string Payload(
        string symbol,
        string state,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset quote,
        decimal price,
        DateTimeOffset inside,
        DateTimeOffset outside)
    {
        var invariant = CultureInfo.InvariantCulture;
        return "{\"chart\":{\"result\":[{\"meta\":{\"currency\":\"BRL\",\"symbol\":\"" + symbol
            + "\",\"marketState\":\"" + state
            + "\",\"regularMarketPrice\":" + price.ToString(invariant)
            + ",\"regularMarketTime\":" + quote.ToUnixTimeSeconds().ToString(invariant)
            + ",\"currentTradingPeriod\":{\"regular\":{\"start\":" + start.ToUnixTimeSeconds().ToString(invariant)
            + ",\"end\":" + end.ToUnixTimeSeconds().ToString(invariant)
            + "}}},\"timestamp\":[" + outside.ToUnixTimeSeconds().ToString(invariant)
            + "," + inside.ToUnixTimeSeconds().ToString(invariant)
            + "],\"indicators\":{\"quote\":[{\"open\":[36.0,36.1],\"high\":[36.2,36.4],\"low\":[35.9,36.0],\"close\":[36.05,36.1],\"volume\":[10,12]}]}}],\"error\":null}}";
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") };
    }

    private sealed class SymbolHandler(Func<string, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            var json = body(path);
            var failed = json.Contains("Not Found", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(failed ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
