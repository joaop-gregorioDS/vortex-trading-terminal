using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Paper;

namespace VortexTrading.Infrastructure.Tests.Trading.Coordination;

/// <summary>
/// Proves the execution scope owner introduced in ADR-037: one report consumer, routing by OrderId,
/// explicit time and deterministic visibility of effects when an operation returns.
/// </summary>
public sealed class ExecutionCoordinatorTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId OtherInstrument = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = Instant(2026, 10, 8, 9, 0, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnedOrdersAreReadableInSubmissionOrder()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var first = Limit(1m, 50m);
        var second = Market(1m);

        await coordinator.SubmitOrderAsync(first, Start, Token);
        await coordinator.SubmitOrderAsync(second, Start, Token);

        // The workflow exposes no order accessor by design, so the scope keeps them for its callers.
        Assert.True(coordinator.TryGetOrder(first.Id, out var resolved));
        Assert.Same(first, resolved);
        Assert.Equal(new[] { first.Id, second.Id }, coordinator.Orders.Select(order => order.Id));

        Assert.True(coordinator.TryGet(first.Id, out var workflow));
        Assert.NotNull(workflow);
        Assert.False(coordinator.TryGetOrder(OrderId.From(Guid.NewGuid()), out _));
    }

    [Fact]
    public async Task SubmittingAnOrderAppliesItsFactsBeforeTheCommandReturns()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var order = Market(10m);

        await coordinator.SubmitOrderAsync(order, Start, Token);

        // Accepted is applied by the drain inside the command, so the effect is visible immediately.
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(1, coordinator.TrackedOrderCount);
        Assert.True(coordinator.WasSubmitted(order.Id));

        coordinator.Observe(Observation(Minutes(1), 100m, 4m));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(4m, order.FilledQuantity.Value);

        coordinator.Observe(Observation(Minutes(2), 100m, 6m));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Equal(Minutes(2), order.CompletedAt);
    }

    [Fact]
    public async Task ReportsAreRoutedToTheOwningOrderOnly()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var first = Market(5m);
        var second = Market(5m);

        await coordinator.SubmitOrderAsync(first, Start, Token);
        await coordinator.SubmitOrderAsync(second, Start, Token);
        coordinator.Observe(Observation(Minutes(1), 100m, 5m));

        // Only the first order, which arrived first, is filled; routing must not cross orders.
        Assert.Equal(OrderStatus.Filled, first.Status);
        Assert.Equal(OrderStatus.Accepted, second.Status);
        Assert.Equal(0m, second.FilledQuantity.Value);

        coordinator.Observe(Observation(Minutes(2), 101m, 5m));
        Assert.Equal(OrderStatus.Filled, second.Status);
        Assert.Equal(101m, second.Fills[0].Price.Value);
    }

    [Fact]
    public async Task CancellingThroughTheScopeReachesCancelled()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var order = Limit(10m, 50m);

        await coordinator.SubmitOrderAsync(order, Start, Token);
        Assert.Equal(OrderStatus.Accepted, order.Status);

        await coordinator.RequestCancellationAsync(order.Id, Minutes(1), Token);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Minutes(1), order.CompletedAt);
        Assert.Empty(order.Fills);
    }

    [Fact]
    public async Task CancellingAnUnknownOrderFailsWithoutInventingState()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RequestCancellationAsync(OrderId.From(Guid.NewGuid()), Start, Token));
    }

    [Fact]
    public async Task ResubmittingTheSameOrderIsRefusedByTheScope()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var order = Limit(10m, 50m);

        await coordinator.SubmitOrderAsync(order, Start, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SubmitOrderAsync(order, Start, Token));
        Assert.Equal(1, coordinator.TrackedOrderCount);
    }

    [Fact]
    public async Task ForeignOrderAndObservationAreRejectedByTheScope()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);

        var foreign = Order.CreateMarket(OrderId.From(Guid.NewGuid()), OtherInstrument, OrderSide.Buy,
            new Quantity(1m), TimeInForce.GTC, Start);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SubmitOrderAsync(foreign, Start, Token));

        Assert.Throws<InvalidOperationException>(() => coordinator.Observe(
            new PaperMarketObservation(Minutes(1), OtherInstrument, new Price(100m), new Quantity(10m))));
    }

    [Fact]
    public async Task ClockSuppliesTheTimeWhenTheCallerDoesNot()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var clock = new FixedClock(Minutes(30));
        var coordinator = new ExecutionCoordinator(Instrument, provider, clock, provider.Reports);
        var order = Limit(1m, 50m);

        // No explicit instant: the injected clock decides, which is the only place a clock is allowed.
        await coordinator.SubmitOrderAsync(order, cancellationToken: Token);

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(Minutes(30), order.LastUpdatedAt);

        await coordinator.RequestCancellationAsync(order.Id, cancellationToken: Token);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Minutes(30), order.CompletedAt);
    }

    [Fact]
    public async Task DrainReportsIsIdempotentWhenNothingIsBuffered()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);

        Assert.Equal(0, coordinator.DrainReports(Start));
        Assert.Equal(0, coordinator.DrainReports(Start));
    }

    [Fact]
    public async Task RepeatedObservationsOfTheSameInstantDoNotRegressApplicationTime()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var coordinator = new ExecutionCoordinator(Instrument, provider, new FixedClock(Start), provider.Reports);
        var order = Market(2m);
        await coordinator.SubmitOrderAsync(order, Start, Token);

        // The same observation time offered twice consumes fresh volume each time; application time
        // never moves backwards, which the domain guards.
        coordinator.Observe(Observation(Minutes(1), 100m, 1m));
        coordinator.Observe(Observation(Minutes(1), 100m, 1m));

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(2m, order.FilledQuantity.Value);
    }

    private static Order Market(decimal quantity) =>
        Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(quantity), TimeInForce.GTC, Start);

    private static Order Limit(decimal quantity, decimal limitPrice) =>
        Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(quantity), new Price(limitPrice), TimeInForce.GTC, Start);

    private static PaperMarketObservation Observation(UtcInstant time, decimal price, decimal volume) =>
        new(time, Instrument, new Price(price), new Quantity(volume));

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

    /// <summary>Controllable clock; the coordinator is the only component allowed to read one.</summary>
    private sealed class FixedClock(UtcInstant now) : IApplicationClock
    {
        public UtcInstant Now() => now;
    }
}
