using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

/// <summary>
/// Chart orders rest on later chart closes. The simulated clock, quote and book stay on the session tape.
/// </summary>
public sealed class ChartTradingSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReferenceBarDoesNotFillAndTheChartCloseLeavesTheTapeAlone()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        var reference = After(created, minutes: 8 * 24 * 60);
        var stop = Order.CreateStop(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(110m), TimeInForce.GTC, created);
        var limit = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(2m), new Price(90m), TimeInForce.GTC, created, OrderOrigin.Chart);

        await session.SubmitAsync(stop, reference, Token);
        await session.SubmitAsync(limit, reference, Token);
        Assert.Equal(OrderStatus.Accepted, stop.Status);
        Assert.Equal(OrderStatus.Accepted, limit.Status);

        // The reference candle is already open. Its close is after the simulated submit instant and
        // would fill the limit, but that candle is the one the order was priced against.
        session.ObserveChart(petr.InstrumentId, reference, 80m, 5m);
        Assert.Equal(OrderStatus.Accepted, stop.Status);
        Assert.Equal(OrderStatus.Accepted, limit.Status);
        Assert.Null(session.CurrentTime);
        Assert.Null(session.LastPriceFor(petr.InstrumentId));
        Assert.Empty(session.ObservationsFor(petr.InstrumentId));
        Assert.Equal(0m, session.ReadBook(petr.InstrumentId).AvailableVolume);
        Assert.Empty(session.ReadBook(petr.InstrumentId).RestingOrders);

        var limitBar = After(reference, 1);
        session.ObserveChart(petr.InstrumentId, limitBar, 85m, 5m);
        Assert.Equal(OrderStatus.Filled, limit.Status);
        Assert.Equal(90m, Assert.Single(limit.Fills).Price.Value);
        Assert.Equal(OrderStatus.Accepted, stop.Status);
        Assert.Null(session.CurrentTime);
        Assert.Equal(0m, session.ReadBook(petr.InstrumentId).AvailableVolume);

        var stopBar = After(reference, 2);
        session.ObserveChart(petr.InstrumentId, stopBar, 125m, 5m);
        Assert.Equal(OrderStatus.Filled, stop.Status);
        Assert.Equal(125m, Assert.Single(stop.Fills).Price.Value);
        Assert.Null(session.LastPriceFor(petr.InstrumentId));
        Assert.Equal(0m, session.ReadBook(petr.InstrumentId).AvailableVolume);
    }

    [Fact]
    public async Task SessionPrintAndChartCloseDoNotFillEachOther()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        var reference = After(created, minutes: 8 * 24 * 60);
        var chart = Order.CreateStop(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(110m), TimeInForce.GTC, created);
        var ticket = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(3m), new Price(50m), TimeInForce.GTC, created);

        await session.SubmitAsync(chart, reference, Token);
        await session.SubmitAsync(ticket, Token);

        // 120 would cross the chart stop if the print and the close shared a tape.
        session.Publish(petr.InstrumentId, After(created, 1), 120m, 1000m);
        Assert.Equal(OrderStatus.Accepted, chart.Status);
        Assert.Empty(chart.Fills);
        Assert.Equal(OrderStatus.Accepted, ticket.Status);
        Assert.Equal(1000m, session.ReadBook(petr.InstrumentId).AvailableVolume);
        Assert.Equal(120m, session.LastPriceFor(petr.InstrumentId)!.Value);

        // 40 would fill the ticket limit if a chart close could reach a session order.
        session.ObserveChart(petr.InstrumentId, After(reference, 1), 40m, 10m);
        Assert.Equal(OrderStatus.Accepted, ticket.Status);
        Assert.Empty(ticket.Fills);
        Assert.Equal(OrderStatus.Accepted, chart.Status);
        Assert.Equal(120m, session.LastPriceFor(petr.InstrumentId)!.Value);
        Assert.Equal(1000m, session.ReadBook(petr.InstrumentId).AvailableVolume);

        session.ObserveChart(petr.InstrumentId, After(reference, 2), 115m, 10m);
        Assert.Equal(OrderStatus.Filled, chart.Status);
        Assert.Equal(115m, Assert.Single(chart.Fills).Price.Value);
        Assert.Equal(OrderStatus.Accepted, ticket.Status);
        Assert.Equal(1000m, session.ReadBook(petr.InstrumentId).AvailableVolume);

        session.Publish(petr.InstrumentId, After(created, 2), 50m, 10m);
        Assert.Equal(OrderStatus.Filled, ticket.Status);
        Assert.Equal(50m, Assert.Single(ticket.Fills).Price.Value);
        Assert.Equal(7m, session.ReadBook(petr.InstrumentId).AvailableVolume);
    }

    [Fact]
    public async Task ChartSellLargerThanTheLongIsAccepted()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var opened = session.Now();
        session.Publish(petr.InstrumentId, opened, 100m, 100m);
        var buy = Order.CreateMarket(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(2m), TimeInForce.GTC, session.Now());
        await session.SubmitAsync(buy, Token);
        session.Publish(petr.InstrumentId, After(opened, 1), 100m, 10m);
        Assert.Equal(OrderStatus.Filled, buy.Status);
        Assert.Equal(2m, session.Portfolio().Positions[petr.InstrumentId].Quantity);

        var created = session.Now();
        var reference = After(created, minutes: 8 * 24 * 60);
        var reserved = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Sell,
            new Quantity(1m), new Price(200m), TimeInForce.GTC, created);
        await session.SubmitAsync(reserved, Token);
        Assert.Equal(OrderStatus.Accepted, reserved.Status);

        var chartSell = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Sell,
            new Quantity(1m), new Price(110m), TimeInForce.GTC, created, OrderOrigin.Chart);
        await session.SubmitAsync(chartSell, reference, Token);
        Assert.Equal(OrderStatus.Accepted, chartSell.Status);

        var extra = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Sell,
            new Quantity(1m), new Price(110m), TimeInForce.GTC, created, OrderOrigin.Chart);
        await session.SubmitAsync(extra, reference, Token);
        Assert.Equal(OrderStatus.Accepted, extra.Status);
        Assert.Null(extra.RejectionReason);
    }

    [Fact]
    public async Task NakedChartSellOpensAShortOnALaterClose()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        var reference = After(created, minutes: 60);
        var sell = Order.CreateStop(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Sell,
            new Quantity(1m), new Price(90m), TimeInForce.GTC, created);

        await session.SubmitAsync(sell, reference, Token);
        Assert.Equal(OrderStatus.Accepted, sell.Status);

        session.ObserveChart(petr.InstrumentId, reference, 95m, 10m);
        Assert.Equal(OrderStatus.Accepted, sell.Status);

        session.ObserveChart(petr.InstrumentId, After(reference, 1), 80m, 10m);
        Assert.Equal(OrderStatus.Filled, sell.Status);
        Assert.Equal(80m, Assert.Single(sell.Fills).Price.Value);
        var position = session.Portfolio().Positions[petr.InstrumentId];
        Assert.Equal(-1m, position.Quantity);
        Assert.Equal(80m, position.AverageCost);
        Assert.Equal(0m, position.RealizedProfitAndLoss);
    }

    [Fact]
    public async Task OnlyAChartOrderCarriesTheReferenceBar()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        var chart = Order.CreateStop(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(110m), TimeInForce.GTC, created);
        var ticket = Order.CreateLimit(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(90m), TimeInForce.GTC, created);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitAsync(chart, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SubmitAsync(ticket, After(created, 1), Token));
        Assert.Equal(OrderStatus.Created, chart.Status);
        Assert.Equal(OrderStatus.Created, ticket.Status);
    }

    private static UtcInstant After(UtcInstant instant, int minutes) =>
        new(instant.Value.AddMinutes(minutes));
}
