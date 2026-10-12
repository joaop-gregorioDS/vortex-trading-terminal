using VortexTrading.Application.MarketData;

namespace VortexTrading.Application.Tests;

public sealed class SimulatedPricePathTests
{
    [Fact]
    public void TheSameTickIndexAlwaysReturnsTheSamePrice()
    {
        var petr = CandleAggregator.Catalog.Single(profile => profile.Ticker == "PETR4");

        var first = SimulatedPricePath.PriceAt(petr, 0);
        var again = SimulatedPricePath.PriceAt(petr, 0);

        Assert.Equal(first, again);
        Assert.Equal(first, CandleAggregator.RoundToTick(first, petr.TickSize, petr.Decimals));
    }

    [Fact]
    public void TheWaveRepeatsAndStaysOnTheInstrumentTick()
    {
        var wing = CandleAggregator.Catalog.Single(profile => profile.Ticker == "WING26");
        var amplitude = (int)decimal.Round(wing.VolatilityFactor / wing.TickSize, 0, MidpointRounding.AwayFromZero);
        var period = amplitude * 4L;

        var start = SimulatedPricePath.PriceAt(wing, 0);
        var later = SimulatedPricePath.PriceAt(wing, 7);

        Assert.Equal(start, SimulatedPricePath.PriceAt(wing, period));
        Assert.NotEqual(start, later);
        Assert.Equal(0m, later % wing.TickSize);
    }

    [Fact]
    public void TwoInstrumentsDoNotShareAPrice()
    {
        var petr = CandleAggregator.Catalog.Single(profile => profile.Ticker == "PETR4");
        var vale = CandleAggregator.Catalog.Single(profile => profile.Ticker == "VALE3");

        Assert.NotEqual(SimulatedPricePath.PriceAt(petr, 1), SimulatedPricePath.PriceAt(vale, 1));
    }

    [Fact]
    public void TheCatalogIsClosed()
    {
        Assert.Equal(15, CandleAggregator.Catalog.Count);
        Assert.Equal(15, CandleAggregator.Catalog.Select(profile => profile.InstrumentId).Distinct().Count());
        Assert.Equal(0.20m, CandleAggregator.Catalog.Single(profile => profile.Ticker == "WING26").PointValue);
        Assert.Equal(10m, CandleAggregator.Catalog.Single(profile => profile.Ticker == "WDOU26").PointValue);
        Assert.Throws<ArgumentException>(() => CandleAggregator.GetProfile("UNKNOWN"));
    }
}
