using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.MarketData;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.MarketData.Replay;

namespace VortexTrading.Infrastructure.Tests;

public sealed class DevelopmentCandleDatasetTests
{
    [Fact]
    public void ConstructionsAreEqualAndHaveExactlyTwoHundredValidVaryingCandles()
    {
        var first = DevelopmentCandleDataset.Create();
        var second = DevelopmentCandleDataset.Create();
        Assert.Equal(200, first.Count);
        var candles = first.Events.Select(e => e.Candle!).ToArray();
        Assert.Equal(candles, second.Events.Select(e => e.Candle!));
        Assert.Equal(DevelopmentCandleDataset.StartTime, candles[0].OpenTime);
        for (var index = 0; index < candles.Length; index++)
        {
            var candle = candles[index];
            Assert.Equal(DevelopmentCandleDataset.InstrumentId, candle.InstrumentId);
            Assert.Equal(DevelopmentCandleDataset.Interval, candle.Interval);
            Assert.Equal(DevelopmentCandleDataset.StartTime.Value.AddMinutes(index), candle.OpenTime.Value);
            Assert.Equal(0, candle.OpenTime.Value.Ticks % TimeSpan.TicksPerMinute);
            Assert.InRange(candle.Open.Value, candle.Low.Value, candle.High.Value);
            Assert.InRange(candle.Close.Value, candle.Low.Value, candle.High.Value);
            Assert.True(candle.Volume.Value >= 0m);
        }
        Assert.True(candles.Select(c => c.Volume.Value).Distinct().Count() > 1);
        Assert.Contains(candles.Zip(candles.Skip(1)), pair => pair.Second.Close.Value > pair.First.Close.Value);
        Assert.Contains(candles.Zip(candles.Skip(1)), pair => pair.Second.Close.Value < pair.First.Close.Value);
    }

    [Fact]
    public async Task RegisteredExistingProviderProducesTheActualDemoSnapshotWithoutPersistence()
    {
        var services = new ServiceCollection();
        services.AddDevelopmentHistoricalCharts();
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IMarketDataProvider>();
        Assert.IsType<HistoricalReplayProvider>(provider);
        var context = container.GetRequiredService<HistoricalChartSourceContext>();
        Assert.True(context.IsDemo);
        Assert.Equal("development-history-v1", context.SourceId);
        var result = await new HistoricalChartQuery(provider, context).ExecuteAsync(
            new HistoricalChartRequest(context.InstrumentId, context.Interval, 200,
                [new SmaSpecification(20), new MacdSpecification(12, 26, 9)]), CancellationToken.None);
        Assert.Equal(200, result.Candles.Count);
        Assert.Equal(new[] { 181, 175, 167, 167 }, result.Indicators.SelectMany(i => i.Result.Series).Select(s => s.Series.Count));
        Assert.True(result.Indicators[1].Result.Series[0].Series.Points.Select(p => p.Value).Distinct().Count() > 1);
    }
}
