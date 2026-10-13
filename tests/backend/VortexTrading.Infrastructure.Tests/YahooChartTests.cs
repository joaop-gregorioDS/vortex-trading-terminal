using System.Net;
using System.Text;
using VortexTrading.Application.MarketData;
using VortexTrading.Infrastructure.MarketData.Yahoo;

namespace VortexTrading.Infrastructure.Tests;

public sealed class YahooChartTests
{
    private const string Payload = """
        {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA"},"timestamp":[1791205200,1791291600,1791378000],"indicators":{"quote":[{"open":[53.15999984741211,null,54.029998779296875],"high":[56.75,54.0,54.79999923706055],"low":[53.0,53.0,53.54999923706055],"close":[55.36000061035156,54.0,54.33000183105469],"volume":[122874900,10,51879700]}]}}],"error":null}}
        """;

    [Fact]
    public void BinaryChartNumbersRoundToTheEquityTickAndNullBarsAreDropped()
    {
        var series = YahooChartParser.Parse(Payload, "PETR4.SA", "1d", 0.01m, 2, 10);

        Assert.Equal("PETR4.SA", series.Symbol);
        Assert.Equal("BRL", series.Currency);
        Assert.Equal(2, series.Bars.Count);
        var first = series.Bars[0];
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791205200), first.OpenTime);
        Assert.Equal(53.16m, first.Open);
        Assert.Equal(56.75m, first.High);
        Assert.Equal(53.00m, first.Low);
        Assert.Equal(55.36m, first.Close);
        Assert.Equal(122874900m, first.Volume);
        Assert.Equal(54.03m, series.Bars[1].Open);
        Assert.Equal(54.33m, series.Bars[1].Close);
    }

    [Fact]
    public void TheNewestBarsAreKeptWhenTheWindowIsShorterThanThePayload()
    {
        var series = YahooChartParser.Parse(Payload, "petr4.sa", "1d", 0.01m, 2, 1);

        var bar = Assert.Single(series.Bars);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791378000), bar.OpenTime);
    }

    [Fact]
    public void TheCommercialDollarScalesIntoMiniPointsBeforeTheTick()
    {
        const string payload = """
            {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"BRL=X"},"timestamp":[1791500400],"indicators":{"quote":[{"open":[4.977200031280518],"high":[5.020699977874756],"low":[4.9766998291015625],"close":[5.019199848175049],"volume":[0]}]}}],"error":null}}
            """;

        var series = YahooChartParser.Parse(payload, "BRL=X", "1d", 0.1m, 1, 10, 1000m);

        var bar = Assert.Single(series.Bars);
        Assert.Equal(4977.2m, bar.Open);
        Assert.Equal(5020.7m, bar.High);
        Assert.Equal(4976.7m, bar.Low);
        Assert.Equal(5019.2m, bar.Close);
    }

    [Fact]
    public void TheSessionFieldsComeFromTheChartMeta()
    {
        const string payload = """
            {"chart":{"result":[{"meta":{"currency":"BRL","symbol":"PETR4.SA","marketState":"REGULAR","regularMarketPrice":36.54999923706055,"regularMarketTime":1790000000,"currentTradingPeriod":{"regular":{"start":1789974000,"end":1789999200}}},"timestamp":[1789974000],"indicators":{"quote":[{"open":[36.1],"high":[36.8],"low":[36.0],"close":[36.4],"volume":[100]}]}}],"error":null}}
            """;

        var document = YahooChartParser.ParseDocument(payload, "PETR4.SA", "1m", 0.01m, 2, 10);

        Assert.Equal("REGULAR", document.MarketState);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789974000), document.RegularStart);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789999200), document.RegularEnd);
        Assert.Equal(36.55m, document.RegularMarketPrice);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), document.RegularMarketTime);
        Assert.Equal(36.40m, Assert.Single(document.Series.Bars).Close);
    }

    [Fact]
    public void ADifferentSymbolOrAnErrorPayloadIsRejected()
    {
        Assert.Throws<YahooChartException>(() =>
            YahooChartParser.Parse(Payload, "VALE3.SA", "1d", 0.01m, 2, 10));
        const string failed = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""";
        Assert.Throws<YahooChartException>(() =>
            YahooChartParser.Parse(failed, "PETR4.SA", "1d", 0.01m, 2, 10));
    }

    [Fact]
    public async Task TheClientRequestsTheSaoPauloSymbolAndTheMatchingRange()
    {
        string? path = null;
        var handler = new StubHandler(Payload, request => path = request.RequestUri?.PathAndQuery);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") };
        var client = new YahooFinanceChartClient(http);
        Assert.True(YahooInterval.TryParse("1d", out var interval));

        var series = await client.LoadAsync("PETR4.SA", interval, 0.01m, 2, 10, CancellationToken.None);

        Assert.Equal("/v8/finance/chart/PETR4.SA?interval=1d&range=1y", path);
        Assert.True(YahooInterval.TryParse("1m", out var minute));
        Assert.Equal("1m", minute.QueryInterval);
        Assert.Equal("1d", minute.Range);
        Assert.Equal(2, series.Bars.Count);
        Assert.Equal(0.01m, CandleAggregator.GetProfile("PETR4").TickSize);
    }

    private sealed class StubHandler(string body, Action<HttpRequestMessage> inspect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
