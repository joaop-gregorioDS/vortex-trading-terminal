using Microsoft.EntityFrameworkCore;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Persistence;

/// <summary>
/// Reproduces the save path that produced a duplicate primary key on trading.orders: a session that
/// restored an order from the database, plus a second order that fills, saving both in one call.
/// </summary>
public sealed class PostgresOrderStoreResaveTests(PersistenceTestFixture fixture) : IClassFixture<PersistenceTestFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavingAnAggregateThatWasLoadedAndThenGainedAFillDoesNotReinsertIt()
    {
        var factory = fixture.CreateContextFactory();
        var store = new PostgresOrderStore(factory);
        var instrument = fixture.TestInstrument;

        // First order already persisted, as it would be after a previous run.
        var restored = Order.CreateMarket(OrderId.From(Guid.NewGuid()), instrument, OrderSide.Buy,
            new Quantity(5m), TimeInForce.GTC, Start);
        restored.Accept(Start);
        restored.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), restored.Id,
            new Quantity(5m), new Price(100m), Start), Start);
        await store.SaveAsync([restored], Token);

        // A session recovers it, exactly as the application does on startup.
        var codeLoaded = (await store.LoadAsync(Token)).Single(order => order.Id == restored.Id);

        // A second order arrives and fills, and the coordinator saves every order it owns.
        var fresh = Order.CreateMarket(OrderId.From(Guid.NewGuid()), instrument, OrderSide.Buy,
            new Quantity(3m), TimeInForce.GTC, Minutes(1));
        fresh.Accept(Minutes(1));
        fresh.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), fresh.Id,
            new Quantity(3m), new Price(101m), Minutes(1)), Minutes(1));

        await store.SaveAsync([codeLoaded, fresh], Token);

        // Both must exist exactly once, and the restored one must not have been duplicated.
        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.Orders.CountAsync(order => order.Id == restored.Id, Token));
        Assert.Equal(1, await context.Orders.CountAsync(order => order.Id == fresh.Id, Token));
    }

    [Fact]
    public async Task SavingTheSameRestoredAggregateRepeatedlyIsIdempotent()
    {
        var factory = fixture.CreateContextFactory();
        var store = new PostgresOrderStore(factory);

        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(4m), new Price(90m), TimeInForce.GTC, Start);
        await store.SaveAsync([order], Token);

        // Save the instance returned by Load, three times, as repeated fills would.
        var loaded = (await store.LoadAsync(Token)).Single(stored => stored.Id == order.Id);
        await store.SaveAsync([loaded], Token);
        await store.SaveAsync([loaded], Token);
        await store.SaveAsync([loaded], Token);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.Orders.CountAsync(stored => stored.Id == order.Id, Token));
    }

    [Fact]
    public async Task TheSameAggregateTwiceInOneCallIsNotInsertedTwice()
    {
        var factory = fixture.CreateContextFactory();
        var store = new PostgresOrderStore(factory);

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(3m), TimeInForce.GTC, Start);

        // Defensive: the scope passes a snapshot of its book, which cannot repeat an id, but a repeated
        // entry must not collide with itself if that collection ever changes shape.
        await store.SaveAsync([order, order], Token);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.Orders.CountAsync(stored => stored.Id == order.Id, Token));
    }

    [Fact]
    public async Task SavingAnOrderWithoutAFillThenWithOneKeepsOneRowAndAddsTheFill()
    {
        var factory = fixture.CreateContextFactory();
        var store = new PostgresOrderStore(factory);

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(5m), TimeInForce.GTC, Start);
        await store.SaveAsync([order], Token);

        order.Accept(Start);
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id,
            new Quantity(5m), new Price(100m), Start), Start);

        // This is the shape that collided in production: the same instance saved again after gaining a
        // fill. It must update the order and insert the fill, never reinsert the order.
        await store.SaveAsync([order], Token);

        await using var context = factory.CreateDbContext();
        Assert.Equal(1, await context.Orders.CountAsync(stored => stored.Id == order.Id, Token));
        Assert.Equal(1, await context.OrderFills.CountAsync(fill => fill.OrderId == order.Id, Token));
    }

    private static UtcInstant Start { get; } = new(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));
}
