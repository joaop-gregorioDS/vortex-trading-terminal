using Microsoft.EntityFrameworkCore;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Persistence;

namespace VortexTrading.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the round trip of the Order aggregate against real PostgreSQL, which is the only way to
/// validate the storage decisions recorded in ADR-035: backing-field materialization, unbounded
/// numeric for exact decimals, and enums stored by name.
/// </summary>
public sealed class OrderPersistenceTests(PersistenceTestFixture fixture) : IClassFixture<PersistenceTestFixture>
{
    private static readonly UtcInstant Created = Instant(2026, 10, 6, 13, 30, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MarketOrderWithNullLimitSurvivesRoundTrip()
    {
        await using var session = fixture.OpenSession();
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(10m), TimeInForce.GTC, Created);

        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        var loaded = await Reload(session.Context, order.Id);
        Assert.Equal(OrderType.Market, loaded.Type);
        Assert.Null(loaded.LimitPrice);
        Assert.Equal(OrderStatus.Created, loaded.Status);
        Assert.Equal(order.InstrumentId, loaded.InstrumentId);
        Assert.Equal(order.Side, loaded.Side);
        Assert.Equal(order.TimeInForce, loaded.TimeInForce);
        Assert.Equal(10m, loaded.Quantity.Value);
        Assert.Empty(loaded.Fills);
        Assert.Equal(10m, loaded.RemainingQuantity.Value);
    }

    [Fact]
    public async Task AcceptedOrderWithFillsPreservesLifecycleAndDerivedQuantities()
    {
        await using var session = fixture.OpenSession();
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(10m), new Price(32.50m), TimeInForce.GTC, Created);
        order.Accept(new UtcInstant(Created.Value.AddMinutes(1)));
        var filledAt = new UtcInstant(Created.Value.AddMinutes(2));
        var fill = new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(4m),
            new Price(32.25m), filledAt);
        Assert.True(order.ApplyFill(fill, filledAt));

        var expectedUpdatedAt = order.LastUpdatedAt;
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        var loaded = await Reload(session.Context, order.Id);
        Assert.Equal(OrderStatus.PartiallyFilled, loaded.Status);
        Assert.Equal(expectedUpdatedAt, loaded.LastUpdatedAt);
        Assert.Equal(32.50m, loaded.LimitPrice!.Value);
        Assert.Equal(4m, loaded.FilledQuantity.Value);
        Assert.Equal(6m, loaded.RemainingQuantity.Value);
        var loadedFill = Assert.Single(loaded.Fills);
        Assert.Equal(fill.ExecutionId, loadedFill.ExecutionId);
        Assert.Equal(fill.Quantity.Value, loadedFill.Quantity.Value);
        Assert.Equal(fill.Price.Value, loadedFill.Price.Value);
        Assert.Equal(fill.ExecutedAt, loadedFill.ExecutedAt);
    }

    [Fact]
    public async Task ExactDecimalValuesKeepTheirValueAndScale()
    {
        await using var session = fixture.OpenSession();
        // 27 fractional digits: any fixed numeric(p,s) with a smaller scale would round this.
        var quantity = 1.000000000000000000000000001m;
        var price = 0.000000000000000000000000003m;
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Sell, new Quantity(quantity), new Price(price), TimeInForce.GTC, Created);
        order.Accept(Created);
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(quantity),
            new Price(price), Created), Created);

        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        var loaded = await Reload(session.Context, order.Id);
        Assert.Equal(quantity, loaded.Quantity.Value);
        Assert.Equal(quantity, loaded.FilledQuantity.Value);
        Assert.Equal(0m, loaded.RemainingQuantity.Value);
        Assert.Equal(price, loaded.LimitPrice!.Value);
        Assert.Equal(price, loaded.Fills[0].Price.Value);
    }

    [Fact]
    public async Task SecondFillWithTheSameExecutionIdIsRejectedByTheDatabase()
    {
        await using var session = fixture.OpenSession();
        var executionId = ExecutionId.From(Guid.NewGuid());
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(10m), TimeInForce.GTC, Created);
        order.Accept(Created);
        order.ApplyFill(new OrderFill(executionId, order.Id, new Quantity(3m), new Price(10m), Created), Created);
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        // A fresh context avoids change-tracker identity conflicts, so the failure must come from
        // the database primary key: one execution identity can never be stored twice.
        await using var second = fixture.OpenSession();
        var other = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(10m), TimeInForce.GTC, Created);
        other.Accept(Created);
        other.ApplyFill(new OrderFill(executionId, other.Id, new Quantity(3m), new Price(10m), Created), Created);
        second.Context.Orders.Add(other);

        await Assert.ThrowsAsync<DbUpdateException>(() => second.Context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task EnumMembersAreStoredByNameNotByOrdinal()
    {
        await using var session = fixture.OpenSession();
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Sell, new Quantity(1m), TimeInForce.GTC, Created);
        order.Accept(Created);
        order.Reject(OrderRejectionReason.ExecutionProviderRejected, Created);
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        // Read the raw column: an integer column would silently break if members were reordered.
        var connection = session.Context.Database.GetDbConnection();
        await session.Context.Database.OpenConnectionAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Status\", \"RejectionReason\" FROM trading.orders WHERE \"Id\" = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = order.Id.Value;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(nameof(OrderStatus.Rejected), reader.GetString(0));
        Assert.Equal(nameof(OrderRejectionReason.ExecutionProviderRejected), reader.GetString(1));
    }

    [Fact]
    public async Task RejectionReasonSurvivesRoundTripForEveryReason()
    {
        await using var session = fixture.OpenSession();
        foreach (var reason in Enum.GetValues<OrderRejectionReason>())
        {
            var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
                OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, Created);
            order.Reject(reason, Created);
            session.Context.Orders.Add(order);
            await session.Context.SaveChangesAsync(Token);
            session.Context.ChangeTracker.Clear();

            var loaded = await Reload(session.Context, order.Id);
            Assert.Equal(OrderStatus.Rejected, loaded.Status);
            Assert.Equal(reason, loaded.RejectionReason);
        }
    }

    [Fact]
    public async Task SubSecondInstantPrecisionIsPreservedToMicroseconds()
    {
        await using var session = fixture.OpenSession();
        // timestamptz resolves to microseconds, so an aligned instant must survive unchanged.
        var preciseCreated = new UtcInstant(new DateTimeOffset(2026, 10, 6, 13, 30, 0, TimeSpan.Zero).AddTicks(1234560));
        var preciseExecuted = new UtcInstant(preciseCreated.Value.AddTicks(10));
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, preciseCreated);
        order.Accept(preciseCreated);
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id, new Quantity(1m),
            new Price(10m), preciseExecuted), preciseExecuted);
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        var loaded = await Reload(session.Context, order.Id);
        Assert.Equal(preciseCreated, loaded.CreatedAt);
        Assert.Equal(preciseExecuted, loaded.LastUpdatedAt);
        Assert.Equal(preciseExecuted, loaded.Fills[0].ExecutedAt);
    }

    [Fact]
    public async Task SubMicrosecondInstantsAreNormalizedInsteadOfSilentlyChanging()
    {
        await using var session = fixture.OpenSession();
        // .NET ticks are 100ns but timestamptz stores microseconds. The converter truncates before
        // the write, so the value that reaches the database is the value that comes back.
        var withExtraTicks = new UtcInstant(new DateTimeOffset(2026, 10, 6, 13, 30, 0, TimeSpan.Zero).AddTicks(1234567));
        var normalized = new UtcInstant(withExtraTicks.Value.AddTicks(-(withExtraTicks.Value.Ticks % 10)));
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, withExtraTicks);
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        var loaded = await Reload(session.Context, order.Id);
        Assert.Equal(normalized, loaded.CreatedAt);
        Assert.NotEqual(withExtraTicks, loaded.CreatedAt);

        // Idempotent: storing the loaded value again does not shift it further.
        loaded.Accept(loaded.CreatedAt);
        await session.Context.SaveChangesAsync(Token);
        var reloaded = await Reload(session.Context, order.Id);
        Assert.Equal(normalized, reloaded.LastUpdatedAt);
    }

    [Fact]
    public async Task EveryOrderStatusSurvivesRoundTrip()
    {
        await using var session = fixture.OpenSession();
        var cases = new (OrderStatus Expected, Action<Order> Advance)[]
        {
            (OrderStatus.Created, _ => { }),
            (OrderStatus.Accepted, order => order.Accept(Created)),
            (OrderStatus.Filled, order =>
            {
                order.Accept(Created);
                order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id,
                    new Quantity(1m), new Price(10m), Created), Created);
            }),
            (OrderStatus.Cancelled, order =>
            {
                order.Accept(Created);
                order.Cancel(Created);
            }),
            (OrderStatus.Rejected, order => order.Reject(OrderRejectionReason.PreTradeValidationFailed, Created))
        };

        foreach (var (expected, advance) in cases)
        {
            var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
                OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, Created);
            advance(order);
            session.Context.Orders.Add(order);
            await session.Context.SaveChangesAsync(Token);
            session.Context.ChangeTracker.Clear();

            var loaded = await Reload(session.Context, order.Id);
            Assert.Equal(expected, loaded.Status);
            Assert.Equal(order.LastUpdatedAt, loaded.LastUpdatedAt);
        }
    }

    [Fact]
    public async Task ModelUsesTheTradingSchemaWithUnboundedNumeric()
    {
        await using var session = fixture.OpenSession();
        var applied = await session.Context.Database.GetAppliedMigrationsAsync(Token);
        Assert.NotEmpty(applied);

        var precision = await session.Context.Database
            .SqlQuery<int?>($"SELECT numeric_precision AS \"Value\" FROM information_schema.columns WHERE table_schema = 'trading' AND table_name = 'orders' AND column_name = 'Quantity'")
            .SingleAsync(Token);
        // Unbounded numeric reports a null declared precision; a declared scale would cap exactness.
        Assert.Null(precision);

        var tables = await session.Context.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'trading' ORDER BY table_name")
            .ToListAsync(Token);
        Assert.Contains("orders", tables);
        Assert.Contains("order_fills", tables);
    }

    [Fact]
    public async Task FillsAreRemovedWithTheirOrder()
    {
        await using var session = fixture.OpenSession();
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), InstrumentId.From(Guid.NewGuid()),
            OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, Created);
        order.Accept(Created);
        order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id,
            new Quantity(1m), new Price(10m), Created), Created);
        session.Context.Orders.Add(order);
        await session.Context.SaveChangesAsync(Token);

        session.Context.Orders.Remove(order);
        await session.Context.SaveChangesAsync(Token);

        var remaining = await session.Context.OrderFills
            .CountAsync(fill => fill.OrderId == order.Id, Token);
        Assert.Equal(0, remaining);
    }

    private static async Task<Order> Reload(VortexTradingDbContext context, OrderId id)
    {
        context.ChangeTracker.Clear();
        return await context.Orders.Include(order => order.Fills)
            .SingleAsync(order => order.Id == id, Token);
    }

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
}
