using VortexTrading.Application.MarketData;
using VortexTrading.Application.Trading;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class RoundTripLedgerTests
{
    [Fact]
    public void PointValueConvertsIndexAndDollarPointsIntoMoney()
    {
        var wing = CandleAggregator.GetProfile("WING26");
        var dollar = CandleAggregator.GetProfile("WDOU26");
        var petr = CandleAggregator.GetProfile("PETR4");
        var day = new DateOnly(2026, 10, 2);

        var trades = RoundTripLedger.Close(
        [
            Fill(petr, OrderSide.Buy, 1m, 30m, At(10, 0)),
            Fill(petr, OrderSide.Sell, 1m, 35m, At(10, 1)),
            Fill(wing, OrderSide.Buy, 1m, 208785m, At(10, 2)),
            Fill(wing, OrderSide.Sell, 1m, 208790m, At(10, 3)),
            Fill(dollar, OrderSide.Buy, 1m, 4957.0m, At(10, 4)),
            Fill(dollar, OrderSide.Sell, 1m, 4957.5m, At(10, 5))
        ]);

        Assert.Equal(5m, trades.Single(trade => trade.Ticker == "PETR4").ProfitAndLoss);
        Assert.Equal(1m, trades.Single(trade => trade.Ticker == "WING26").ProfitAndLoss);
        Assert.Equal(5m, trades.Single(trade => trade.Ticker == "WDOU26").ProfitAndLoss);

        var summary = RoundTripLedger.Summarize(trades, 100000m, 0m, day);
        Assert.Equal(11m, summary.RealizedProfitAndLoss);
        Assert.Equal(100011m, summary.Equity);
        Assert.Equal(3, summary.Wins);
        Assert.Equal(0, summary.Losses);
        Assert.Null(summary.ProfitFactor);
    }

    [Fact]
    public void DrawdownAndWinRateComeFromTheClosedSequence()
    {
        var petr = CandleAggregator.GetProfile("PETR4");
        var trades = RoundTripLedger.Close(
        [
            Fill(petr, OrderSide.Buy, 1m, 10m, At(11, 0), "a"),
            Fill(petr, OrderSide.Sell, 1m, 11m, At(11, 1), "b"),
            Fill(petr, OrderSide.Buy, 1m, 11m, At(11, 2), "c"),
            Fill(petr, OrderSide.Sell, 1m, 9m, At(11, 3), "d")
        ]);

        var summary = RoundTripLedger.Summarize(trades, 100000m, 0m, new DateOnly(2026, 10, 2));

        Assert.Equal(0.5m, summary.WinRate);
        Assert.Equal(1, summary.Wins);
        Assert.Equal(1, summary.Losses);
        Assert.Equal(2m, summary.MaxDrawdown);
        Assert.Equal(0.5m, summary.ProfitFactor);
        Assert.Equal(-1m, summary.RealizedProfitAndLoss);
    }

    [Fact]
    public void AShortRoundTripRealizesTheCover()
    {
        var petr = CandleAggregator.GetProfile("PETR4");
        var trades = RoundTripLedger.Close(
        [
            Fill(petr, OrderSide.Sell, 2m, 100m, At(12, 0), "open"),
            Fill(petr, OrderSide.Buy, 2m, 90m, At(12, 1), "cover")
        ]);

        var trade = Assert.Single(trades);
        Assert.Equal(OrderSide.Sell, trade.OpenedSide);
        Assert.Equal(20m, trade.ProfitAndLoss);
        Assert.Equal(100m, trade.EntryPrice);
        Assert.Equal(90m, trade.ExitPrice);
    }

    private static TradeFill Fill(AssetMarketProfile profile, OrderSide side, decimal quantity, decimal price,
        UtcInstant time, string? id = null) =>
        new(profile.InstrumentId, profile.Ticker, side, quantity, price, profile.PointValue, time,
            id ?? Guid.NewGuid().ToString("D"));

    private static UtcInstant At(int hour, int minute) =>
        new(new DateTimeOffset(2026, 10, 2, hour, minute, 0, TimeSpan.Zero));
}
