using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Persistence;

/// <summary>
/// Pins down what a restored order looks like to each reader, because the portfolio and the
/// order-by-id endpoint disagreeing is a real defect rather than a detail.
/// </summary>
public sealed class RestoreConsistencyTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PartiallyFilled)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    public void ARestoredOrderIsVisibleToEveryReader(OrderStatus status)
    {
        var store = new SeededStore(BuildOrder(status));
        var session = new PaperTradingSession(store);
        var id = store.Order.Id;

        _ = session.RestoredOrderCount;

        Assert.True(session.Coordinator.TryGetOrder(id, out var direct), "TryGetOrder must find it");
        Assert.NotNull(direct);
        Assert.Contains(session.Coordinator.Orders, order => order.Id == id);
        Assert.Equal(status, direct!.Status);

        session.Dispose();
    }

    private static Order BuildOrder(OrderStatus status)
    {
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), TimeInForce.GTC, Start);

        if (status == OrderStatus.Created)
        {
            return order;
        }

        order.Accept(Start);
        if (status is OrderStatus.PartiallyFilled or OrderStatus.Filled)
        {
            var quantity = status == OrderStatus.Filled ? 10m : 4m;
            order.ApplyFill(new OrderFill(ExecutionId.From(Guid.NewGuid()), order.Id,
                new Quantity(quantity), new Price(100m), Start), Start);
        }

        if (status == OrderStatus.Cancelled)
        {
            order.Cancel(Start);
        }

        return order;
    }

    private sealed class SeededStore(Order order) : IOrderStore
    {
        internal Order Order { get; } = order;

        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([Order]);
    }
}
