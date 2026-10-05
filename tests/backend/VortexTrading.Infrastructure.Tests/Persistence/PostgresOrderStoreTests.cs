using Microsoft.EntityFrameworkCore;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the PostgreSQL order store against the real database: a wholesale save inserts, a second
/// save updates without duplicating, and a load returns the aggregate with its lifecycle and fills.
/// </summary>
/// <remarks>
/// Skipped when no connection string is configured, like the other persistence tests, so CI without a
/// database stays green.
/// </remarks>
public sealed class PostgresOrderStoreTests(PersistenceTestFixture fixture) : IClassFixture<PersistenceTestFixture>
{
    
    private static readonly UtcInstant Start = Instant(2026, 10, 12, 10, 0, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavingTheSameAggregateTwiceUpdatesInsteadOfDuplicating()
    {
        var store = new PostgresOrderStore(fixture.CreateContextFactory());
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(10m), new Price(100m), TimeInForce.GTC, Start);

        await store.SaveAsync([order], Token);

        // Second save with lifecycle and a new fill: the existing row must be updated, not reinserted.
        order.Accept(Minutes(1));
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(4m),
            new Price(99m), Minutes(1)), Minutes(1));
        await store.SaveAsync([order], Token);

        await using var context = fixture.CreateContextFactory().CreateDbContext();
        Assert.Equal(1, await context.Orders.CountAsync(stored => stored.Id == order.Id, Token));
        Assert.Equal(1, await context.OrderFills.CountAsync(fill => fill.OrderId == order.Id, Token));
    }

    [Fact]
    public async Task LoadReturnsLifecycleFillsAndDerivedQuantities()
    {
        var store = new PostgresOrderStore(fixture.CreateContextFactory());
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(10m), new Price(100m), TimeInForce.GTC, Start);
        order.Accept(Minutes(1));
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(4m),
            new Price(99m), Minutes(1)), Minutes(1));
        await store.SaveAsync([order], Token);

        var loaded = (await store.LoadAsync(Token)).Single(stored => stored.Id == order.Id);

        Assert.Equal(OrderStatus.PartiallyFilled, loaded.Status);
        Assert.Equal(4m, loaded.FilledQuantity.Value);
        Assert.Equal(6m, loaded.RemainingQuantity.Value);
        Assert.Equal(Minutes(1), loaded.LastUpdatedAt);
        Assert.Single(loaded.Fills);
        Assert.Equal(99m, loaded.Fills[0].Price.Value);
    }

    [Fact]
    public async Task AFreshFillOnAnExistingAggregateIsPersisted()
    {
        var store = new PostgresOrderStore(fixture.CreateContextFactory());
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), fixture.TestInstrument, OrderSide.Buy,
            new Quantity(10m), TimeInForce.GTC, Start);
        order.Accept(Minutes(1));
        await store.SaveAsync([order], Token);

        // The aggregate already has a row; a later fill must still be written.
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(10m),
            new Price(101m), Minutes(2)), Minutes(2));
        await store.SaveAsync([order], Token);

        var loaded = (await store.LoadAsync(Token)).Single(stored => stored.Id == order.Id);
        Assert.Equal(OrderStatus.Filled, loaded.Status);
        Assert.Equal(10m, loaded.FilledQuantity.Value);
        Assert.Equal(0m, loaded.RemainingQuantity.Value);
        Assert.Equal(101m, loaded.Fills[0].Price.Value);
    }

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
}
