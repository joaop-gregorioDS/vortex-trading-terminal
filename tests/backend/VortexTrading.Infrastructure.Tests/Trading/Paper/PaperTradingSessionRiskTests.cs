using VortexTrading.Application.Risk;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

public sealed class PaperTradingSessionRiskTests
{
    private static readonly InstrumentId Instrument = PaperTradingSession.InstrumentId;
    private static readonly UtcInstant Start = Instant(2026, 10, 1, 10, 0, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrderExceedingMaxQuantityIsRejectedPreTradeAndRecorded()
    {
        var riskOptions = new RiskOptions
        {
            Enabled = true,
            MaxOrderQuantity = 50m
        };
        var riskEvaluator = RiskEvaluator.FromOptions(riskOptions);

        using var session = new PaperTradingSession(riskEvaluator: riskEvaluator);
        session.Publish(Start, 100m, 1000m);

        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(75m), new Price(100m), TimeInForce.GTC, Start);

        await session.SubmitAsync(order, Token);

        // Status is Rejected with PreTradeValidationFailed
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(OrderRejectionReason.PreTradeValidationFailed, order.RejectionReason);
        Assert.Empty(order.Fills);

        // Order is retained in the coordinator's order book
        Assert.True(session.Coordinator.TryGetOrder(order.Id, out var stored));
        Assert.Same(order, stored);

        // It did NOT get sent to the execution provider workflow
        Assert.False(session.Coordinator.TryGet(order.Id, out _));
        Assert.Equal(0, session.RestingOrderCount);
    }

    [Fact]
    public async Task OrderExceedingNotionalIsRejectedPreTrade()
    {
        var riskOptions = new RiskOptions
        {
            Enabled = true,
            MaxOrderNotional = 1000m
        };
        var riskEvaluator = RiskEvaluator.FromOptions(riskOptions);

        using var session = new PaperTradingSession(riskEvaluator: riskEvaluator);
        session.Publish(Start, 100m, 1000m);

        // 15 * 100 = 1500 > 1000
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(15m), new Price(100m), TimeInForce.GTC, Start);

        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(OrderRejectionReason.PreTradeValidationFailed, order.RejectionReason);
    }

    [Fact]
    public async Task MarketOrderWithoutReferencePriceIsRejectedPreTrade()
    {
        var riskOptions = new RiskOptions
        {
            Enabled = true,
            RequireReferencePriceForMarketOrders = true
        };
        var riskEvaluator = RiskEvaluator.FromOptions(riskOptions);

        using var session = new PaperTradingSession(riskEvaluator: riskEvaluator);
        // Do not publish any observation, so LastPrice remains null

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), TimeInForce.GTC, Start);

        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(OrderRejectionReason.PreTradeValidationFailed, order.RejectionReason);
    }

    [Fact]
    public async Task ValidOrderPassesRiskEvaluationAndIsAccepted()
    {
        var riskOptions = new RiskOptions
        {
            Enabled = true,
            MaxOrderQuantity = 100m,
            MaxOrderNotional = 50000m
        };
        var riskEvaluator = RiskEvaluator.FromOptions(riskOptions);

        using var session = new PaperTradingSession(riskEvaluator: riskEvaluator);
        session.Publish(Start, 100m, 1000m);

        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), new Price(99m), TimeInForce.GTC, Start);

        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Null(order.RejectionReason);
        Assert.Equal(1, session.RestingOrderCount);
    }

    [Fact]
    public async Task PreTradeRejectedOrderIsPersistedWhenStoreIsConfigured()
    {
        var riskOptions = new RiskOptions
        {
            Enabled = true,
            MaxOrderQuantity = 10m
        };
        var riskEvaluator = RiskEvaluator.FromOptions(riskOptions);
        var store = new RecordingOrderStore();

        using var session = new PaperTradingSession(store: store, riskEvaluator: riskEvaluator);
        session.Publish(Start, 100m, 1000m);

        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(50m), new Price(100m), TimeInForce.GTC, Start);

        await session.SubmitAsync(order, Token);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(1, store.SaveCalls);
        Assert.Contains(order.Id.Value, store.LastSavedIds);
    }

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

    private sealed class RecordingOrderStore : IOrderStore
    {
        internal int SaveCalls { get; private set; }
        internal IReadOnlyList<Guid> LastSavedIds { get; private set; } = [];

        public Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastSavedIds = orders.Select(order => order.Id.Value).ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>([]);

        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
