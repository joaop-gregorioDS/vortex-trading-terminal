using System.Runtime.CompilerServices;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Paper;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

/// <summary>
/// Proves the matching rules approved in ADR-036 against the frozen provider boundary: explicit
/// caller time, finite liquidity with partial fills, limit orders filling at the limit, deterministic
/// execution identity and no look-ahead.
/// </summary>
public sealed class PaperExecutionProviderTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId OtherInstrument = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = Instant(2026, 10, 7, 10, 0, 0);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidSubmissionIsAcknowledgedBeforeAnyFill()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 10m, 99m);

        await provider.SubmitAsync(Submission(order), Start, Token);
        var reports = await Collect(provider, 1);

        var accepted = Assert.Single(reports);
        Assert.Equal(ExecutionReportKind.Accepted, accepted.Kind);
        Assert.Equal(order.Id, accepted.OrderId);
        Assert.Equal(Start, accepted.ObservedAt);
        Assert.Null(accepted.Fill);
    }

    [Fact]
    public async Task MarketOrderFillsAtTheObservedReferencePrice()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        await provider.SubmitAsync(Submission(Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument,
            OrderSide.Buy, new Quantity(5m), TimeInForce.GTC, Start)), Start, Token);
        await Collect(provider, 1);

        provider.Observe(Observation(Minutes(1), 101.25m, 1000m));
        var reports = await Collect(provider, 1);

        var fill = Assert.Single(reports);
        Assert.Equal(ExecutionReportKind.Fill, fill.Kind);
        Assert.Equal(5m, fill.Fill!.Quantity.Value);
        Assert.Equal(101.25m, fill.Fill.Price.Value);
        Assert.Equal(Minutes(1), fill.Fill.ExecutedAt);
    }

    [Fact]
    public async Task MarketOrderNeverFillsAtAPriceObservedBeforeSubmission()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        provider.Observe(Observation(Minutes(0), 100m, 1000m));

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(5m), TimeInForce.GTC, Start);
        await provider.SubmitAsync(Submission(order), Start, Token);
        var immediate = await Collect(provider, 1, TimeSpan.FromMilliseconds(150));

        // A price that existed before the intent must not fill it: matching needs a strictly later
        // observation, which is the same causal rule the replay uses.
        Assert.Equal(ExecutionReportKind.Accepted, Assert.Single(immediate).Kind);
        Assert.Equal(1, provider.RestingOrderCount);
    }

    [Fact]
    public async Task BuyLimitFillsAtTheLimitPriceOnlyWhenTheReferenceReachesIt()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 10m, 100m);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        // Reference above the limit: not reached, nothing fills.
        provider.Observe(Observation(Minutes(1), 100.50m, 1000m));
        Assert.Empty(await Collect(provider, 1, TimeSpan.FromMilliseconds(150)));

        // Touching the limit fills, and always at the limit price, never at the better reference.
        provider.Observe(Observation(Minutes(2), 100m, 1000m));
        var fill = Assert.Single(await Collect(provider, 1));
        Assert.Equal(100m, fill.Fill!.Price.Value);
        Assert.Equal(10m, fill.Fill.Quantity.Value);
    }

    [Fact]
    public async Task SellLimitFillsAtTheLimitPriceOnlyWhenTheReferenceReachesIt()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Sell, 4m, 105m);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        provider.Observe(Observation(Minutes(1), 104.75m, 1000m));
        Assert.Empty(await Collect(provider, 1, TimeSpan.FromMilliseconds(150)));

        provider.Observe(Observation(Minutes(2), 106m, 1000m));
        var fill = Assert.Single(await Collect(provider, 1));
        Assert.Equal(105m, fill.Fill!.Price.Value);
    }

    [Fact]
    public async Task InsufficientVolumeProducesAPartialFillAndTheRemainderStaysResting()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(100m), TimeInForce.GTC, Start);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        provider.Observe(Observation(Minutes(1), 100m, 30m));
        var first = Assert.Single(await Collect(provider, 1));
        Assert.Equal(30m, first.Fill!.Quantity.Value);
        Assert.Equal(1, provider.RestingOrderCount);

        provider.Observe(Observation(Minutes(2), 100m, 70m));
        var second = Assert.Single(await Collect(provider, 1));
        Assert.Equal(70m, second.Fill!.Quantity.Value);
        Assert.Equal(0, provider.RestingOrderCount);
    }

    [Fact]
    public async Task AccumulatedPartialFillsNeverExceedTheSubmittedQuantity()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), TimeInForce.GTC, Start);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        // Three observations with more than enough aggregate volume for a 10 unit order.
        provider.Observe(Observation(Minutes(1), 100m, 6m));
        provider.Observe(Observation(Minutes(2), 100m, 6m));
        provider.Observe(Observation(Minutes(3), 100m, 6m));

        var reports = await Collect(provider, 3);
        var fills = reports.Where(report => report.Kind == ExecutionReportKind.Fill).ToArray();
        Assert.Equal(10m, fills.Sum(report => report.Fill!.Quantity.Value));
        Assert.Equal(new[] { 6m, 4m }, fills.Select(report => report.Fill!.Quantity.Value));
        Assert.Equal(0, provider.RestingOrderCount);
    }

    [Fact]
    public async Task EarlierArrivalConsumesAvailableVolumeFirst()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var first = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(8m), TimeInForce.GTC, Start);
        var second = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(8m), TimeInForce.GTC, Start);

        await provider.SubmitAsync(Submission(first), Start, Token);
        await provider.SubmitAsync(Submission(second), Start, Token);
        await Collect(provider, 2);

        provider.Observe(Observation(Minutes(1), 100m, 10m));
        var reports = await Collect(provider, 2);

        // FIFO: the first order takes all 8, the second gets only the remaining 2.
        var fills = reports.Where(report => report.Kind == ExecutionReportKind.Fill).ToArray();
        Assert.Equal(first.Id, fills[0].OrderId);
        Assert.Equal(8m, fills[0].Fill!.Quantity.Value);
        Assert.Equal(second.Id, fills[1].OrderId);
        Assert.Equal(2m, fills[1].Fill!.Quantity.Value);
        Assert.Equal(1, provider.RestingOrderCount);
    }

    [Fact]
    public async Task ExecutionIdentityIsDeterministicForTheSameOrderAndSequence()
    {
        static async Task<ExecutionId> RunAsync(OrderId orderId)
        {
            using var provider = new PaperExecutionProvider(Instrument);
            var order = Order.CreateMarket(orderId, Instrument, OrderSide.Buy,
                new Quantity(5m), TimeInForce.GTC, Start);
            await provider.SubmitAsync(Submission(order), Start, Token);
            provider.Observe(Observation(Minutes(1), 100m, 1000m));
            var reports = await Collect(provider, 2);
            return reports.Single(report => report.Kind == ExecutionReportKind.Fill).Fill!.ExecutionId;
        }

        var orderId = OrderId.From(Guid.NewGuid());

        // Two independent runs over the same order identity must yield the same execution identity,
        // which is what lets the domain deduplicate a redelivered fill instead of duplicating it.
        var first = await RunAsync(orderId);
        var second = await RunAsync(orderId);
        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first.Value);

        // A different order must not collide with it.
        Assert.NotEqual(first, await RunAsync(OrderId.From(Guid.NewGuid())));
    }

    [Fact]
    public async Task RestingRemainderCarriesAcrossObservationsAtTheLimitPrice()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 10m, 100m);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        // The remainder rests across observations and keeps filling at the limit, never better.
        provider.Observe(Observation(Minutes(1), 99m, 4m));
        var first = Assert.Single(await Collect(provider, 1));
        Assert.Equal(4m, first.Fill!.Quantity.Value);
        Assert.Equal(100m, first.Fill.Price.Value);
        Assert.Equal(Minutes(1), first.Fill.ExecutedAt);

        provider.Observe(Observation(Minutes(2), 98m, 6m));
        var second = Assert.Single(await Collect(provider, 1));
        Assert.Equal(6m, second.Fill!.Quantity.Value);
        Assert.Equal(100m, second.Fill.Price.Value);
        Assert.Equal(0, provider.RestingOrderCount);
    }

    [Fact]
    public async Task CancellingARestingOrderYieldsAnEffectiveCancellation()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 5m, 50m);
        await provider.SubmitAsync(Submission(order), Start, Token);
        await Collect(provider, 1);

        await provider.RequestCancelAsync(order.Id, Minutes(1), Token);
        var cancelled = Assert.Single(await Collect(provider, 1));

        Assert.Equal(ExecutionReportKind.Cancelled, cancelled.Kind);
        Assert.Equal(order.Id, cancelled.OrderId);
        Assert.Equal(0, provider.RestingOrderCount);
    }

    [Fact]
    public async Task CancellingAnUnknownOrFilledOrderIsRefusedAndNeverFabricatesCancellation()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var unknown = OrderId.From(Guid.NewGuid());
        var refused = await Assert.ThrowsAsync<ExecutionProviderException>(
            () => provider.RequestCancelAsync(unknown, Minutes(1), Token));
        Assert.Equal(ExecutionProviderFailureKind.CancellationRefused, refused.FailureKind);
        Assert.False(refused.OutcomeUncertain);
        Assert.Equal(unknown, refused.OrderId);

        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(5m), TimeInForce.GTC, Start);
        await provider.SubmitAsync(Submission(order), Start, Token);
        provider.Observe(Observation(Minutes(1), 100m, 1000m));
        await Collect(provider, 2);

        var afterFill = await Assert.ThrowsAsync<ExecutionProviderException>(
            () => provider.RequestCancelAsync(order.Id, Minutes(2), Token));
        Assert.Equal(ExecutionProviderFailureKind.CancellationRefused, afterFill.FailureKind);
        Assert.Empty(await Collect(provider, 1, TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task DuplicateOrderIdentityIsRefusedInsteadOfSilentlyMerging()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 5m, 50m);
        await provider.SubmitAsync(Submission(order), Start, Token);

        var duplicate = await Assert.ThrowsAsync<ExecutionProviderException>(
            () => provider.SubmitAsync(Submission(order), Start, Token));
        Assert.Equal(ExecutionProviderFailureKind.InvalidProviderResponse, duplicate.FailureKind);
        Assert.False(duplicate.OutcomeUncertain);
        Assert.Equal(1, provider.RestingOrderCount);
    }

    [Theory]
    [InlineData("submission instrument")]
    [InlineData("observation instrument")]
    public async Task ForeignInstrumentIsRejected(string kind)
    {
        using var provider = new PaperExecutionProvider(Instrument);
        if (kind == "submission instrument")
        {
            var foreign = Order.CreateMarket(OrderId.From(Guid.NewGuid()), OtherInstrument, OrderSide.Buy,
                new Quantity(1m), TimeInForce.GTC, Start);
            await Assert.ThrowsAsync<ArgumentException>(() => provider.SubmitAsync(Submission(foreign), Start, Token));
        }
        else
        {
            Assert.Throws<ArgumentException>(() =>
                provider.Observe(new PaperMarketObservation(Minutes(1), OtherInstrument, new Price(100m), new Quantity(10m))));
        }
    }

    [Fact]
    public async Task OutOfOrderObservationFailsInsteadOfRewritingHistory()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        provider.Observe(Observation(Minutes(5), 100m, 100m));

        var backwards = Assert.Throws<ArgumentException>(() => provider.Observe(Observation(Minutes(4), 101m, 100m)));
        Assert.Contains("non-decreasing", backwards.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmissionTimeCannotPrecedeCreation()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = LimitOrder(OrderSide.Buy, 5m, 50m);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.SubmitAsync(Submission(order), new UtcInstant(Start.Value.AddSeconds(-1)), Token));
    }

    [Fact]
    public void ProviderKeyMustBeStableAndTrimmed()
    {
        Assert.Equal("paper", new PaperExecutionProvider(Instrument).ProviderKey);
        Assert.Equal("custom", new PaperExecutionProvider(Instrument, "custom").ProviderKey);
        Assert.Throws<ArgumentException>(() => new PaperExecutionProvider(Instrument, " padded "));
        Assert.Throws<ArgumentException>(() => new PaperExecutionProvider(Instrument, "  "));
    }

    [Fact]
    public async Task ReportsDriveTheOrderWorkflowEndToEnd()
    {
        // The point of this provider is to feed ADR-034. This drives the real workflow with the real
        // provider, proving acceptance, partial fill and terminal fill are applied through Domain
        // guards without the workflow reading the stream itself.
        using var provider = new PaperExecutionProvider(Instrument);
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), TimeInForce.GTC, Start);
        var workflow = new OrderExecutionWorkflow(order, provider);

        await workflow.SubmitOrderAsync(Start, Token);
        provider.Observe(Observation(Minutes(1), 100m, 4m));
        Assert.True(workflow.ApplyReport(await Next(provider), Minutes(1)));
        Assert.Equal(OrderStatus.Accepted, order.Status);

        Assert.True(workflow.ApplyReport(await Next(provider), Minutes(1)));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(4m, order.FilledQuantity.Value);
        Assert.Equal(6m, order.RemainingQuantity.Value);

        provider.Observe(Observation(Minutes(2), 100.5m, 6m));
        Assert.True(workflow.ApplyReport(await Next(provider), Minutes(2)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(Minutes(2), order.CompletedAt);
        Assert.Equal(2, order.Fills.Count);
    }

    [Fact]
    public async Task CancelledRestingOrderReachesCancelledInTheWorkflow()
    {
        using var provider = new PaperExecutionProvider(Instrument);
        var order = Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, OrderSide.Buy,
            new Quantity(10m), new Price(50m), TimeInForce.GTC, Start);
        var workflow = new OrderExecutionWorkflow(order, provider);

        await workflow.SubmitOrderAsync(Start, Token);
        Assert.True(workflow.ApplyReport(await Next(provider), Start));
        Assert.Equal(OrderStatus.Accepted, order.Status);

        await workflow.RequestCancellationAsync(Minutes(1), Token);
        Assert.True(workflow.ApplyReport(await Next(provider), Minutes(1)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Minutes(1), order.CompletedAt);
        Assert.Empty(order.Fills);
    }

    private static async Task<ExecutionReport> Next(PaperExecutionProvider provider)
    {
        await foreach (var report in provider.ReadReportsAsync(TestContext.Current.CancellationToken))
        {
            return report;
        }

        throw new InvalidOperationException("Report stream completed without a report.");
    }

    private static OrderSubmission Submission(Order order) =>
        new(order.Id, order.InstrumentId, order.Side, order.Type, order.Quantity, order.LimitPrice,
            order.TimeInForce, order.CreatedAt);

    private static Order LimitOrder(OrderSide side, decimal quantity, decimal limitPrice) =>
        Order.CreateLimit(OrderId.From(Guid.NewGuid()), Instrument, side, new Quantity(quantity),
            new Price(limitPrice), TimeInForce.GTC, Start);

    private static PaperMarketObservation Observation(UtcInstant time, decimal price, decimal volume) =>
        new(time, Instrument, new Price(price), new Quantity(volume));

    private static IReadOnlyList<ExecutionReport> Empty { get; } = [];

    /// <summary>
    /// Collects at least <paramref name="count"/> reports, or returns an empty list when the stream
    /// produces nothing before the timeout. Used to assert the absence of a fill.
    /// </summary>
    private static async Task<IReadOnlyList<ExecutionReport>> Collect(PaperExecutionProvider provider,
        int count, TimeSpan? timeout = null)
    {
        var reports = new List<ExecutionReport>();
        using var source = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));

        try
        {
            await foreach (var report in provider.ReadReportsAsync(source.Token))
            {
                reports.Add(report);
                if (reports.Count >= count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return reports.Count == 0 ? Empty : reports;
        }

        return reports;
    }

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));

    private static UtcInstant Instant(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
}
