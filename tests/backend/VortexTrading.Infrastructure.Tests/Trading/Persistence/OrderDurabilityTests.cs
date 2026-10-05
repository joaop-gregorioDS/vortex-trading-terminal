using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Paper;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Persistence;

/// <summary>
/// Proves the durability wiring added in ADR-040: the scope persists after a mutation, does not touch
/// the store when nothing changed, and can restore what it previously wrote.
/// </summary>
public sealed class OrderDurabilityTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Start = Instant(2026, 10, 11, 9, 0, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubmittingPersistsTheOrder()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var store = new RecordingOrderStore();
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start),
            provider.Reports, store);
        var order = Limit(10m, 50m);

        await coordinator.SubmitOrderAsync(order, Start, Token);

        Assert.Equal(1, store.SaveCalls);
        Assert.Contains(order.Id.Value, store.LastSavedIds);
    }

    [Fact]
    public async Task CancellationPersistsTheNewState()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var store = new RecordingOrderStore();
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start),
            provider.Reports, store);
        var order = Limit(10m, 50m);

        await coordinator.SubmitOrderAsync(order, Start, Token);
        await coordinator.RequestCancellationAsync(order.Id, Minutes(1), Token);

        Assert.Equal(2, store.SaveCalls);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task AnObservationThatChangesNothingDoesNotWriteToTheStore()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var store = new RecordingOrderStore();
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start),
            provider.Reports, store);
        var order = Limit(10m, 50m);

        await coordinator.SubmitOrderAsync(order, Start, Token);
        var savesAfterSubmit = store.SaveCalls;

        // The observation touches the book but fills nothing, so rewriting every order would be pure
        // write amplification: observations arrive on a timer and most ticks change nothing.
        coordinator.Observe(Observation(Minutes(1), 1000m, 100m));
        Assert.Equal(savesAfterSubmit, store.SaveCalls);

        // One that does fill must persist.
        coordinator.Observe(Observation(Minutes(2), 50m, 100m));
        Assert.Equal(savesAfterSubmit + 1, store.SaveCalls);
        Assert.True(order.FilledQuantity.Value > 0m);
    }

    [Fact]
    public async Task RestoredOrdersAreReadableAndCounted()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var store = new RecordingOrderStore();
        var persisted = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(5m), new Price(90m), TimeInForce.GTC, Start);
        store.Seed(persisted);

        var session = new PaperTradingSession(store);

        // Reading the portfolio triggers recovery, so a read after a restart reflects the history.
        var portfolio = session.Portfolio();
        Assert.Equal(1, session.RestoredOrderCount);
        Assert.True(session.Coordinator.TryGetOrder(persisted.Id, out var restored));
        Assert.Same(persisted, restored);

        // The order never filled, so the position is empty but the order is known.
        Assert.Empty(portfolio.Positions);
        session.Dispose();
    }

    [Fact]
    public async Task AFailedLoadDoesNotPreventTheSessionFromWorking()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        // The load fails while saving still works, which is what a database that recovers mid-life
        // looks like. A store that fails everything would break the trade itself and prove nothing
        // about recovery.
        var store = new LoadFailingOrderStore();
        var session = new PaperTradingSession(store);

        // The persistence foundation states that a valid configuration must boot with the database
        // down and report the failure through readiness, so recovery degrades instead of throwing.
        Assert.Equal(0, session.RestoredOrderCount);

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, Start);
        session.Publish(Start, 100m, 10m);
        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(1, store.SaveCalls);
        session.Dispose();
    }

    private static Order Limit(decimal quantity, decimal limitPrice) =>
        Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(quantity), new Price(limitPrice), TimeInForce.GTC, Start);

    private static PaperMarketObservation Observation(UtcInstant time, decimal price, decimal volume) =>
        new(time, Instrument, new Price(price), new Quantity(volume));

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

    private sealed class FixedClock(UtcInstant now) : IApplicationClock
    {
        public UtcInstant Now() => now;
    }

    /// <summary>Captures what the scope asked to persist, without a database.</summary>
    private sealed class RecordingOrderStore : IOrderStore
    {
        private readonly List<Order> _stored = [];

        internal int SaveCalls { get; private set; }

        internal IReadOnlyList<Guid> LastSavedIds { get; private set; } = [];

        internal void Seed(Order order) => _stored.Add(order);

        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastSavedIds = orders.Select(order => order.Id.Value).ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(_stored.ToArray());
    }

    /// <summary>Simulates a database that is unreachable for reads but accepts writes.</summary>
    private sealed class LoadFailingOrderStore : IOrderStore
    {
        internal int SaveCalls { get; private set; }

        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<Order>>(new InvalidOperationException("Database is unavailable."));
    }
}
