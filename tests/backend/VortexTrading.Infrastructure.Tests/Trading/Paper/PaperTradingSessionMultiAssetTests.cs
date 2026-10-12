using VortexTrading.Application.MarketData;
using VortexTrading.Application.Risk;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

public sealed class PaperTradingSessionMultiAssetTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APrintForOneInstrumentDoesNotFillAnother()
    {
        var vale = CandleAggregator.Catalog.Single(profile => profile.Ticker == "VALE3");
        using var session = new PaperTradingSession();
        var created = session.Now();
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), vale.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(80m), TimeInForce.GTC, created);

        await session.SubmitAsync(order, Token);
        Assert.Equal(OrderStatus.Accepted, order.Status);

        // This price would fill the VALE3 limit if both assets shared one book.
        session.Publish(PaperTradingSession.InstrumentId, After(created, 1), 50m, 1000m);
        Assert.Equal(0m, order.FilledQuantity.Value);

        session.Publish(vale.InstrumentId, After(created, 2), 80m, 10m);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(vale.InstrumentId, order.InstrumentId);
        var portfolio = session.Portfolio();
        Assert.True(portfolio.Positions.ContainsKey(vale.InstrumentId));
        Assert.False(portfolio.Positions.ContainsKey(PaperTradingSession.InstrumentId));
    }

    [Fact]
    public async Task MiniIndexNotionalUsesThePointValue()
    {
        var wing = CandleAggregator.Catalog.Single(profile => profile.Ticker == "WING26");
        using var session = new PaperTradingSession(riskEvaluator: RiskEvaluator.FromOptions(new RiskOptions()));
        var created = session.Now();

        var one = Order.CreateLimit(OrderId.From(Guid.NewGuid()), wing.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(208785m), TimeInForce.GTC, created);
        await session.SubmitAsync(one, Token);
        Assert.Equal(OrderStatus.Accepted, one.Status);

        var two = Order.CreateLimit(OrderId.From(Guid.NewGuid()), wing.InstrumentId, OrderSide.Buy,
            new Quantity(2m), new Price(208785m), TimeInForce.GTC, created);
        await session.SubmitAsync(two, Token);

        Assert.Equal(OrderStatus.Rejected, two.Status);
        Assert.Equal(OrderRejectionReason.PreTradeValidationFailed, two.RejectionReason);
        Assert.False(session.Coordinator.TryGetOrder(one.Id, out _));
        Assert.True(session.TryGetOrder(one.Id, out var stored));
        Assert.Equal(wing.InstrumentId, stored!.InstrumentId);
    }

    [Fact]
    public void ARestoredOrderReturnsToItsOwnBook()
    {
        var vale = CandleAggregator.Catalog.Single(profile => profile.Ticker == "VALE3");
        var created = new UtcInstant(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), vale.InstrumentId, OrderSide.Buy,
            new Quantity(3m), new Price(70m), TimeInForce.GTC, created);
        order.Accept(created);
        var store = new SeededStore(order);

        using var session = new PaperTradingSession(store);

        Assert.Equal(1, session.RestoredOrderCount);
        Assert.False(session.Coordinator.TryGetOrder(order.Id, out _));
        Assert.True(session.TryGetOrder(order.Id, out var restored));
        Assert.Equal(OrderStatus.Accepted, restored!.Status);
    }

    [Fact]
    public async Task ResetDemonstrationDropsTheBookAndTheNextRoundOpensAtTheSessionStart()
    {
        var vale = CandleAggregator.Catalog.Single(profile => profile.Ticker == "VALE3");
        var store = new CountingStore();
        using var session = new PaperTradingSession(store);
        var created = session.Now();
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), vale.InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(80m), TimeInForce.GTC, created);

        await session.SubmitAsync(order, Token);
        session.Publish(vale.InstrumentId, After(created, 30), 80m, 10m);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.NotEmpty(session.Orders);
        Assert.NotEmpty(session.ObservationsFor(vale.InstrumentId));

        session.ResetDemonstration();

        Assert.Equal(1, store.DeleteCalls);
        Assert.Empty(session.Orders);
        Assert.Empty(session.ObservationsFor(vale.InstrumentId));
        Assert.Null(session.CurrentTime);
        Assert.Empty(session.Portfolio().Positions);
        Assert.Equal(0m, session.Portfolio().RealizedProfitAndLoss);
        Assert.Equal(0, session.RestingOrderCount);

        var open = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        session.PublishFeedRound(TimeSpan.FromSeconds(15));
        var first = Assert.Single(session.ObservationsFor(vale.InstrumentId));
        Assert.Equal(open, first.EventTime.Value);

        session.PublishFeedRound(TimeSpan.FromSeconds(15));
        Assert.Equal(2, session.ObservationsFor(vale.InstrumentId).Count);
        Assert.Equal(open.AddSeconds(15), session.ObservationsFor(vale.InstrumentId)[1].EventTime.Value);
        Assert.Empty(session.Orders);
    }

    [Fact]
    public void PrintsStayAvailablePastAFifteenMinuteWindow()
    {
        using var session = new PaperTradingSession();
        for (var index = 0; index < 301; index++)
        {
            session.PublishFeedRound(TimeSpan.FromSeconds(15));
        }

        var wing = CandleAggregator.Catalog.Single(profile => profile.Ticker == "WING26");
        Assert.Equal(301, session.ObservationsFor(wing.InstrumentId).Count);
        Assert.True(PaperTradingSession.MaximumObservations >= 24 * 60 * 60 / 15);
    }

    [Fact]
    public void AnUnknownPersistedOrderIsSkipped()
    {
        var created = new UtcInstant(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var foreign = new InstrumentId(Guid.NewGuid());
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), foreign, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, created);
        var store = new SeededStore(order);

        using var session = new PaperTradingSession(store);

        Assert.Equal(0, session.RestoredOrderCount);
        Assert.False(session.TryGetOrder(order.Id, out _));
        Assert.Empty(session.Orders);
    }

    private static UtcInstant After(UtcInstant start, int minutes) =>
        new(start.Value.AddMinutes(minutes));

    private sealed class SeededStore(Order order) : IOrderStore
    {
        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([order]);

        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CountingStore : IOrderStore
    {
        public int DeleteCalls { get; private set; }

        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([]);

        public Task DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }
}
