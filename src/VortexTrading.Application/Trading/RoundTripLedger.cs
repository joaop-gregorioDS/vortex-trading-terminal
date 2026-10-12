using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading;

/// <summary>One fill, in the order the session applied it.</summary>
public sealed record TradeFill(
    InstrumentId InstrumentId,
    string Ticker,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal PointValue,
    UtcInstant ExecutedAt,
    string ExecutionId);

/// <summary>A long that was reduced. P&amp;L is money, after the instrument point value.</summary>
public sealed record ClosedRoundTrip(
    string Id,
    string Ticker,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal ProfitAndLoss,
    UtcInstant ExitedAt,
    OrderSide OpenedSide = OrderSide.Buy);

/// <summary>Performance figures derived from closed round trips. Ratios are null when they cannot be formed.</summary>
public sealed record PerformanceSummary(
    decimal StartingCash,
    decimal CurrentBalance,
    decimal Equity,
    decimal RealizedProfitAndLoss,
    decimal DailyProfitAndLoss,
    decimal WinRate,
    int Wins,
    int Losses,
    int TotalTrades,
    decimal? ProfitFactor,
    decimal? Payoff,
    decimal MaxDrawdown,
    ClosedRoundTrip? Best,
    ClosedRoundTrip? Worst,
    IReadOnlyList<EquityStep> EquityCurve);

/// <summary>Cumulative realized result after one closing fill.</summary>
public sealed record EquityStep(UtcInstant Time, decimal Equity, decimal RealizedProfitAndLoss);

/// <summary>
/// Rebuilds closed day-trade round trips from fills. A sell reduces a long and the excess opens a
/// short; a buy reduces that short the same way. Each reduction is one closed trade.
/// </summary>
public static class RoundTripLedger
{
    private const int MoneyScale = 8;

    public static IReadOnlyList<ClosedRoundTrip> Close(IReadOnlyList<TradeFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);

        var open = new Dictionary<InstrumentId, OpenLot>();
        var closed = new List<ClosedRoundTrip>();
        foreach (var fill in fills.OrderBy(item => item.ExecutedAt).ThenBy(item => item.ExecutionId, StringComparer.Ordinal))
        {
            if (!open.TryGetValue(fill.InstrumentId, out var lot))
            {
                lot = new OpenLot();
                open[fill.InstrumentId] = lot;
            }

            var signed = fill.Side == OrderSide.Buy ? fill.Quantity : ExactDecimal.Negate(fill.Quantity);
            if (lot.Quantity == 0m || Math.Sign(lot.Quantity) == Math.Sign(signed))
            {
                lot.Cost = ExactDecimal.Add(lot.Cost, ExactDecimal.Multiply(signed, fill.Price, MoneyScale));
                lot.Quantity = ExactDecimal.Add(lot.Quantity, signed);
                continue;
            }

            var heldAbs = lot.Quantity < 0m ? ExactDecimal.Negate(lot.Quantity) : lot.Quantity;
            var closing = fill.Quantity > heldAbs ? heldAbs : fill.Quantity;
            var opening = ExactDecimal.Subtract(fill.Quantity, closing);
            var average = ExactDecimal.Divide(lot.Cost, lot.Quantity, MoneyScale + 2);
            var points = lot.Quantity > 0m
                ? ExactDecimal.Multiply(ExactDecimal.Subtract(fill.Price, average), closing, MoneyScale)
                : ExactDecimal.Multiply(ExactDecimal.Subtract(average, fill.Price), closing, MoneyScale);
            var pnl = ExactDecimal.Multiply(points, fill.PointValue, MoneyScale);
            var openedSide = lot.Quantity > 0m ? OrderSide.Buy : OrderSide.Sell;
            closed.Add(new ClosedRoundTrip(
                fill.ExecutionId, fill.Ticker, closing, average, fill.Price, pnl, fill.ExecutedAt, openedSide));

            if (opening == 0m)
            {
                var remainingAbs = ExactDecimal.Subtract(heldAbs, closing);
                lot.Quantity = remainingAbs == 0m
                    ? 0m
                    : lot.Quantity > 0m ? remainingAbs : ExactDecimal.Negate(remainingAbs);
                lot.Cost = lot.Quantity == 0m
                    ? 0m
                    : ExactDecimal.Subtract(lot.Cost, ExactDecimal.Multiply(
                        average, lot.Quantity > 0m || remainingAbs == 0m ? closing : ExactDecimal.Negate(closing), MoneyScale));
                continue;
            }

            var opened = fill.Side == OrderSide.Buy ? opening : ExactDecimal.Negate(opening);
            lot.Quantity = opened;
            lot.Cost = ExactDecimal.Multiply(opened, fill.Price, MoneyScale);
        }

        return closed;
    }

    public static PerformanceSummary Summarize(
        IReadOnlyList<ClosedRoundTrip> trades,
        decimal startingCash,
        decimal unrealizedProfitAndLoss,
        DateOnly? sessionDay)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var realized = 0m;
        var daily = 0m;
        var grossProfit = 0m;
        var grossLoss = 0m;
        var wins = 0;
        var losses = 0;
        decimal peak = 0m;
        decimal maxDrawdown = 0m;
        ClosedRoundTrip? best = null;
        ClosedRoundTrip? worst = null;
        var curve = new List<EquityStep>(trades.Count);

        foreach (var trade in trades)
        {
            realized = ExactDecimal.Add(realized, trade.ProfitAndLoss);
            if (trade.ProfitAndLoss > 0m)
            {
                wins++;
                grossProfit = ExactDecimal.Add(grossProfit, trade.ProfitAndLoss);
            }
            else if (trade.ProfitAndLoss < 0m)
            {
                losses++;
                grossLoss = ExactDecimal.Add(grossLoss, ExactDecimal.Multiply(trade.ProfitAndLoss, -1m, MoneyScale));
            }

            if (sessionDay is { } day && DateOnly.FromDateTime(trade.ExitedAt.Value.UtcDateTime) == day)
            {
                daily = ExactDecimal.Add(daily, trade.ProfitAndLoss);
            }

            if (realized > peak)
            {
                peak = realized;
            }

            var drawdown = ExactDecimal.Subtract(peak, realized);
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }

            if (best is null || trade.ProfitAndLoss > best.ProfitAndLoss)
            {
                best = trade;
            }

            if (worst is null || trade.ProfitAndLoss < worst.ProfitAndLoss)
            {
                worst = trade;
            }

            curve.Add(new EquityStep(trade.ExitedAt, ExactDecimal.Add(startingCash, realized), realized));
        }

        var total = trades.Count;
        decimal? profitFactor = grossLoss == 0m ? null : ExactDecimal.Divide(grossProfit, grossLoss, MoneyScale);
        decimal? payoff = wins == 0 || losses == 0
            ? null
            : ExactDecimal.Divide(
                ExactDecimal.Divide(grossProfit, wins, MoneyScale),
                ExactDecimal.Divide(grossLoss, losses, MoneyScale),
                MoneyScale);

        return new PerformanceSummary(
            startingCash,
            ExactDecimal.Add(startingCash, realized),
            ExactDecimal.Add(ExactDecimal.Add(startingCash, realized), unrealizedProfitAndLoss),
            realized,
            daily,
            total == 0 ? 0m : ExactDecimal.Divide(wins, total, MoneyScale),
            wins,
            losses,
            total,
            profitFactor,
            payoff,
            maxDrawdown,
            best,
            worst,
            curve);
    }

    private sealed class OpenLot
    {
        public decimal Quantity { get; set; }

        public decimal Cost { get; set; }
    }
}
