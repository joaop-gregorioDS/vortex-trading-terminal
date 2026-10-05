using System.Reflection;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Trading.Execution;

public sealed class OrderExecutionWorkflowTests
{
    private static readonly OrderId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId Instrument = new(new Guid("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void ConstructionRejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new OrderExecutionWorkflow(null!, new ScriptedProvider()));
        Assert.Throws<ArgumentNullException>(() => new OrderExecutionWorkflow(CreateOrder(), null!));
    }

    [Theory]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PartiallyFilled)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public void ConstructionRequiresCreatedOrder(OrderStatus status)
    {
        Assert.Throws<InvalidOperationException>(() => new OrderExecutionWorkflow(OrderAt(status), new ScriptedProvider()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" scripted")]
    [InlineData("scripted ")]
    public void ConstructionRejectsInvalidProviderIdentity(string? key)
    {
        Assert.Throws<ArgumentException>(() => new OrderExecutionWorkflow(CreateOrder(), new ScriptedProvider { ProviderKey = key! }));
    }

    [Fact]
    public void PublicShapeHasOnlyTheThreeOperationsAndNoResourceOrOrderAccessor()
    {
        var type = typeof(OrderExecutionWorkflow);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetProperties());
        Assert.Equal(new[] { "ApplyReport", "RequestCancellationAsync", "SubmitOrderAsync" },
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.Name).Order().ToArray());
        Assert.DoesNotContain(typeof(IDisposable), type.GetInterfaces());
        Assert.DoesNotContain(typeof(IAsyncDisposable), type.GetInterfaces());
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderType.Market)]
    [InlineData(OrderSide.Sell, OrderType.Market)]
    [InlineData(OrderSide.Buy, OrderType.Limit)]
    [InlineData(OrderSide.Sell, OrderType.Limit)]
    public async Task SubmissionPreservesEveryCanonicalIntentComponentAndExplicitTime(OrderSide side, OrderType type)
    {
        var order = CreateOrder(side, type);
        var provider = new ScriptedProvider();
        var workflow = new OrderExecutionWorkflow(order, provider);
        await workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        var command = Assert.Single(provider.Submissions);
        Assert.Same(order.Id, command.Submission.OrderId);
        Assert.Same(order.InstrumentId, command.Submission.InstrumentId);
        Assert.Equal(side, command.Submission.Side);
        Assert.Equal(type, command.Submission.Type);
        Assert.Same(order.Quantity, command.Submission.Quantity);
        Assert.Same(order.LimitPrice, command.Submission.LimitPrice);
        Assert.Equal(order.TimeInForce, command.Submission.TimeInForce);
        Assert.Same(order.CreatedAt, command.Submission.CreatedAt);
        Assert.Equal(At(1), command.At);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(At(0), order.LastUpdatedAt);
        Assert.Null(order.CompletedAt);
        Assert.Equal(0, provider.ReaderCalls);
    }

    [Fact]
    public void InvalidSubmissionTimeDoesNotDispatchOrConsumeTheFirstAttempt()
    {
        var provider = new ScriptedProvider();
        var workflow = new OrderExecutionWorkflow(CreateOrder(), provider);
        Assert.Throws<ArgumentNullException>(() => { _ = workflow.SubmitOrderAsync(null!, TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentException>(() => { _ = workflow.SubmitOrderAsync(At(-1), TestContext.Current.CancellationToken); });
        Assert.Empty(provider.Submissions);
        Assert.True(workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PendingSubmissionBlocksOverlapButAllowsAcceptanceBeforeCommandCompletion()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider { SubmitResult = () => source.Task };
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        var command = workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        Assert.Same(source.Task, command);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(ExecutionReport.FromAccepted(Id, At(90)), At(2)));
        Assert.False(command.IsCompleted);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        source.SetResult();
        await command;
        Assert.Single(provider.Submissions);
        Assert.Equal(At(2), order.LastUpdatedAt);
    }

    [Fact]
    public async Task PendingSubmissionAllowsInitialRejectionBeforeCommandCompletion()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, new ScriptedProvider { SubmitResult = () => source.Task });
        var command = workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        Assert.True(workflow.ApplyReport(ExecutionReport.FromRejected(Id, At(90)), At(2)));
        Assert.False(command.IsCompleted);
        source.SetResult();
        await command;
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(OrderRejectionReason.ExecutionProviderRejected, order.RejectionReason);
    }

    [Fact]
    public async Task SuccessfulCommandDoesNotAuthorizeResubmissionWhileInitialReportIsUnapplied()
    {
        var provider = new ScriptedProvider();
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        await workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.Single(provider.Submissions);
    }

    [Theory]
    [InlineData(ExecutionProviderFailureKind.Unavailable)]
    [InlineData(ExecutionProviderFailureKind.TransportFailure)]
    [InlineData(ExecutionProviderFailureKind.Timeout)]
    [InlineData(ExecutionProviderFailureKind.InvalidProviderResponse)]
    [InlineData(ExecutionProviderFailureKind.AdapterFailure)]
    [InlineData(ExecutionProviderFailureKind.OutcomeUnknown)]
    public async Task TechnicalSubmitFailurePreservesExceptionAndNeverRejectsOrder(ExecutionProviderFailureKind kind)
    {
        var error = Failure(kind, true);
        var provider = new ScriptedProvider { SubmitResult = () => Task.FromException(error) };
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        Assert.Same(error, await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken)));
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Null(order.RejectionReason);
        Assert.Equal(At(0), order.LastUpdatedAt);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.Single(provider.Submissions);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    public async Task UncertainSubmissionStillAllowsLaterConfirmedFacts(ExecutionReportKind kind)
    {
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, new ScriptedProvider
        {
            SubmitResult = () => Task.FromException(Failure(ExecutionProviderFailureKind.Timeout, true))
        });
        await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(Report(kind), At(3)));
        Assert.Equal(kind switch
        {
            ExecutionReportKind.Accepted => OrderStatus.Accepted,
            ExecutionReportKind.Rejected => OrderStatus.Rejected,
            _ => OrderStatus.Cancelled
        }, order.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefiniteNoEffectFailureAllowsOnlyExplicitRetryWithTheSameSnapshot(bool synchronousThrow)
    {
        var error = Failure(ExecutionProviderFailureKind.Unavailable, false);
        var provider = new ScriptedProvider
        {
            SubmitResult = synchronousThrow ? () => throw error : () => Task.FromException(error)
        };
        var workflow = new OrderExecutionWorkflow(CreateOrder(), provider);
        Assert.Same(error, await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken)));
        Assert.Single(provider.Submissions);
        provider.SubmitResult = () => Task.CompletedTask;
        await workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Submissions.Count);
        Assert.Same(provider.Submissions[0].Submission, provider.Submissions[1].Submission);
        Assert.Equal(At(2), provider.Submissions[1].At);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(3), TestContext.Current.CancellationToken); });
    }

    [Fact]
    public async Task SynchronousUncertainFailureBlocksRetryAndAllowsLaterAcceptance()
    {
        var error = Failure(ExecutionProviderFailureKind.TransportFailure, true);
        var provider = new ScriptedProvider { SubmitResult = () => throw error };
        var workflow = new OrderExecutionWorkflow(CreateOrder(), provider);
        Assert.Same(error, await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2)));
        Assert.Single(provider.Submissions);
    }

    [Fact]
    public async Task FactAppliedBeforeCommandFailurePreventsRetryDespiteNoEffectClaim()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider { SubmitResult = () => source.Task };
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        var command = workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
        source.SetException(Failure(ExecutionProviderFailureKind.Unavailable, false));
        await Assert.ThrowsAsync<ExecutionProviderException>(() => command);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(3), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 1m), At(90)), At(3)));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    public async Task DefiniteNoEffectSubmissionFailureDoesNotEstablishReportContext(ExecutionReportKind kind)
    {
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, new ScriptedProvider
        {
            SubmitResult = () => Task.FromException(Failure(ExecutionProviderFailureKind.Unavailable, false))
        });
        await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken));
        AssertProtocolFailure(() => workflow.ApplyReport(Report(kind), At(2)));
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    [InlineData(ExecutionReportKind.Fill)]
    public void ReportsWithoutSubmissionFailClosedWithoutMutation(ExecutionReportKind kind)
    {
        var order = CreateOrder();
        var provider = new ScriptedProvider();
        var workflow = new OrderExecutionWorkflow(order, provider);
        AssertProtocolFailure(() => workflow.ApplyReport(Report(kind), At(2)));
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(At(0), order.LastUpdatedAt);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(3), TestContext.Current.CancellationToken); });
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(kind), At(3)));
        Assert.Empty(provider.Submissions);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    [InlineData(ExecutionReportKind.Fill)]
    public async Task WrongOrderReportFailsCorrelationAndStopsFurtherContinuation(ExecutionReportKind kind)
    {
        var (order, provider, workflow) = await Submitted();
        var other = new OrderId(new Guid("44444444-4444-4444-4444-444444444444"));
        var report = kind switch
        {
            ExecutionReportKind.Accepted => ExecutionReport.FromAccepted(other, At(90)),
            ExecutionReportKind.Rejected => ExecutionReport.FromRejected(other, At(90)),
            ExecutionReportKind.Cancelled => ExecutionReport.FromCancelled(other, At(90)),
            _ => ExecutionReport.FromFill(new OrderFill(new ExecutionId(new Guid(1, 0, 0, new byte[8])),
                other, new Quantity(1m), new Price(100m), At(-10)), At(90))
        };
        var error = AssertProtocolFailure(() => workflow.ApplyReport(report, At(2)));
        Assert.Equal(Id, error.OrderId);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(3)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken); });
        Assert.Single(provider.Submissions);
    }

    [Fact]
    public async Task InvalidReportArgumentsDoNotConsumeAcceptance()
    {
        var (order, _, workflow) = await Submitted();
        Assert.Throws<ArgumentNullException>(() => workflow.ApplyReport(null!, At(2)));
        Assert.Throws<ArgumentNullException>(() => workflow.ApplyReport(Report(ExecutionReportKind.Accepted), null!));
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2)));
        Assert.Equal(OrderStatus.Accepted, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PartiallyFilled)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public async Task KnownAcceptanceRedeliveryIsNoOpAfterLegitimateLifecycleEvolution(OrderStatus status)
    {
        var (order, _, workflow) = await Accepted();
        Evolve(workflow, status);
        var last = order.LastUpdatedAt;
        var completed = order.CompletedAt;
        Assert.False(workflow.ApplyReport(ExecutionReport.FromAccepted(Id, At(99)), At(-1)));
        Assert.Equal(last, order.LastUpdatedAt);
        Assert.Equal(completed, order.CompletedAt);
        Assert.Equal(status, order.Status);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    public async Task UnknownLateAcceptanceAfterInitialTerminalFactIsProtocolFailure(ExecutionReportKind initial)
    {
        var (order, _, workflow) = await Submitted();
        workflow.ApplyReport(Report(initial), At(2));
        var status = order.Status;
        AssertProtocolFailure(() => workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(3)));
        Assert.Equal(status, order.Status);
        Assert.Equal(At(2), order.CompletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedProviderRejectionAndItsRedeliveryAreAppliedExactlyOnce(bool afterAcceptance)
    {
        var (order, _, workflow) = afterAcceptance ? await Accepted() : await Submitted();
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Rejected), At(3)));
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(OrderRejectionReason.ExecutionProviderRejected, order.RejectionReason);
        Assert.False(workflow.ApplyReport(ExecutionReport.FromRejected(Id, At(99)), At(-1)));
        Assert.Equal(At(3), order.LastUpdatedAt);
        Assert.Equal(At(3), order.CompletedAt);
    }

    [Fact]
    public void PreTradeRejectionCannotBeOwnedAsAPreviouslyAppliedProviderRejection()
    {
        var order = CreateOrder();
        order.Reject(OrderRejectionReason.PreTradeValidationFailed, At(1));
        Assert.Throws<InvalidOperationException>(() => new OrderExecutionWorkflow(order, new ScriptedProvider()));
        Assert.Equal(OrderRejectionReason.PreTradeValidationFailed, order.RejectionReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task RejectionAfterAnyExecutionFailsWithoutChangingEconomics(int amount)
    {
        var (order, _, workflow) = await Accepted();
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, amount), At(90)), At(3));
        var status = order.Status;
        AssertProtocolFailure(() => workflow.ApplyReport(Report(ExecutionReportKind.Rejected), At(4)));
        Assert.Equal(status, order.Status);
        Assert.Equal((decimal)amount, order.FilledQuantity.Value);
        Assert.Equal(At(3), order.LastUpdatedAt);
        Assert.Null(order.RejectionReason);
    }

    [Fact]
    public async Task MultipleCanonicalFillsDrivePartialAndExactFinalCompletion()
    {
        var (order, _, workflow) = await Accepted();
        var first = Fill(1, 2m);
        Assert.True(workflow.ApplyReport(ExecutionReport.FromFill(first, At(90)), At(3)));
        Assert.Same(first, Assert.Single(order.Fills));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(8m, order.RemainingQuantity.Value);
        Assert.Null(order.CompletedAt);
        Assert.True(workflow.ApplyReport(ExecutionReport.FromFill(Fill(2, 3m), At(90)), At(4)));
        Assert.True(workflow.ApplyReport(ExecutionReport.FromFill(Fill(3, 5m), At(90)), At(5)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(At(5), order.CompletedAt);
        Assert.Equal(new[] { 2m, 3m, 5m }, order.Fills.Select(fill => fill.Quantity.Value).ToArray());
    }

    [Theory]
    [InlineData(OrderStatus.PartiallyFilled)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task DomainOwnsEquivalentFillRedeliveryIncludingTerminalAndOlderApplicationTime(OrderStatus status)
    {
        var (order, _, workflow) = await Accepted();
        var amount = status == OrderStatus.Filled ? 10m : 2m;
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, amount), At(90)), At(3));
        if (status == OrderStatus.Cancelled)
        {
            workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(4));
        }

        var last = order.LastUpdatedAt;
        var completed = order.CompletedAt;
        Assert.False(workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, amount), At(99)), At(-1)));
        Assert.Single(order.Fills);
        Assert.Equal(last, order.LastUpdatedAt);
        Assert.Equal(completed, order.CompletedAt);
        Assert.Equal(status, order.Status);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("executionTime")]
    public async Task ConflictingExecutionIdPreservesDomainExceptionAndFailsIntegrity(string conflict)
    {
        var (order, provider, workflow) = await Accepted();
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(3));
        var bad = new OrderFill(new ExecutionId(new Guid(1, 0, 0, new byte[8])), Id,
            new Quantity(conflict == "quantity" ? 3m : 2m), new Price(conflict == "price" ? 99m : 100m),
            conflict == "executionTime" ? At(8) : At(-10));
        Assert.Throws<DomainException>(() => workflow.ApplyReport(ExecutionReport.FromFill(bad, At(90)), At(4)));
        Assert.Equal(2m, order.FilledQuantity.Value);
        Assert.Single(order.Fills);
        Assert.Equal(At(3), order.LastUpdatedAt);
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(5)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(5), TestContext.Current.CancellationToken); });
        Assert.Empty(provider.Cancellations);
    }

    [Theory]
    [InlineData(OrderSide.Buy, 11, 100)]
    [InlineData(OrderSide.Buy, 1, 101)]
    [InlineData(OrderSide.Sell, 1, 99)]
    public async Task DomainEconomicGuardsArePreservedWithoutPublishingInvalidFill(OrderSide side, int amount, int price)
    {
        var order = CreateOrder(side, OrderType.Limit);
        var workflow = new OrderExecutionWorkflow(order, new ScriptedProvider());
        await workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
        Assert.Throws<DomainException>(() => workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, amount, price), At(90)), At(3)));
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Empty(order.Fills);
        Assert.Equal(At(2), order.LastUpdatedAt);
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(ExecutionReport.FromFill(Fill(2, 1m), At(90)), At(4)));
    }

    [Fact]
    public async Task FillBeforeAcceptanceIsNotReorderedOrSynthesized()
    {
        var (order, _, workflow) = await Submitted();
        Assert.Throws<DomainException>(() => workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 1m), At(90)), At(2)));
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Empty(order.Fills);
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(3)));
    }

    [Theory]
    [InlineData(ExecutionReportKind.Cancelled)]
    [InlineData(ExecutionReportKind.Rejected)]
    public async Task NewFillAfterTerminalFactIsDomainFailure(ExecutionReportKind terminal)
    {
        var (order, _, workflow) = await Accepted();
        workflow.ApplyReport(Report(terminal), At(3));
        var status = order.Status;
        Assert.Throws<DomainException>(() => workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 1m), At(90)), At(4)));
        Assert.Equal(status, order.Status);
        Assert.Empty(order.Fills);
        Assert.Equal(At(3), order.CompletedAt);
    }

    [Fact]
    public async Task NewFillAfterFullExecutionIsDomainFailure()
    {
        var (order, _, workflow) = await Accepted();
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 10m), At(90)), At(3));
        Assert.Throws<DomainException>(() => workflow.ApplyReport(ExecutionReport.FromFill(Fill(2, 1m), At(90)), At(4)));
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Single(order.Fills);
        Assert.Equal(At(3), order.CompletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EligibleCancellationForwardsIdentityTimeAndTokenWithoutCancellingOrder(bool partial)
    {
        var (order, provider, workflow) = await Accepted();
        if (partial)
        {
            workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(3));
        }

        var status = order.Status;
        var last = order.LastUpdatedAt;
        using var tokenSource = new CancellationTokenSource();
        await workflow.RequestCancellationAsync(At(4), tokenSource.Token);
        var request = Assert.Single(provider.Cancellations);
        Assert.Same(Id, request.Id);
        Assert.Equal(At(4), request.At);
        Assert.Equal(tokenSource.Token, request.Token);
        Assert.Equal(status, order.Status);
        Assert.Equal(last, order.LastUpdatedAt);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(5), TestContext.Current.CancellationToken); });
        Assert.Equal(0, provider.ReaderCalls);
    }

    [Fact]
    public async Task PendingCancellationBlocksOverlapAndAllowsConfirmedReportBeforeTaskCompletion()
    {
        var (order, provider, workflow) = await Accepted();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.CancelResult = () => source.Task;
        var command = workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken);
        Assert.Same(source.Task, command);
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(4), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(4)));
        Assert.False(command.IsCompleted);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        source.SetResult();
        await command;
        Assert.Single(provider.Cancellations);
        Assert.Equal(At(4), order.CompletedAt);
    }

    [Fact]
    public async Task AcceptanceWhileSubmitPendingAllowsAnExplicitCancellationRequest()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider { SubmitResult = () => source.Task };
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        var submit = workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
        await workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken);
        Assert.Single(provider.Cancellations);
        Assert.False(submit.IsCompleted);
        source.SetResult();
        await submit;
    }

    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public async Task IneligibleOrderCommandsAreRejectedBeforeProviderInvocation(OrderStatus status)
    {
        var (order, provider, workflow) = await Submitted();
        if (status != OrderStatus.Created)
        {
            workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
            Evolve(workflow, status);
            Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(5), TestContext.Current.CancellationToken); });
        }

        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(5), TestContext.Current.CancellationToken); });
        Assert.Equal(status, order.Status);
        Assert.Empty(provider.Cancellations);
        Assert.Single(provider.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefinitiveCancellationRefusalPreservesExceptionAndAllowsExplicitNewRequest(bool synchronousThrow)
    {
        var (order, provider, workflow) = await Accepted();
        var error = Failure(ExecutionProviderFailureKind.CancellationRefused, false);
        provider.CancelResult = synchronousThrow ? () => throw error : () => Task.FromException(error);
        Assert.Same(error, await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken)));
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(At(2), order.LastUpdatedAt);
        Assert.Single(provider.Cancellations);
        provider.CancelResult = () => Task.CompletedTask;
        await workflow.RequestCancellationAsync(At(4), TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Cancellations.Count);
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(5)));
    }

    [Theory]
    [InlineData(ExecutionProviderFailureKind.Timeout)]
    [InlineData(ExecutionProviderFailureKind.TransportFailure)]
    [InlineData(ExecutionProviderFailureKind.CancellationRefused)]
    public async Task UncertainCancellationBlocksRetryButStillAcceptsFillsAndEffectiveCancellation(ExecutionProviderFailureKind kind)
    {
        var (order, provider, workflow) = await Accepted();
        var error = Failure(kind, true);
        provider.CancelResult = () => Task.FromException(error);
        Assert.Same(error, await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(4), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(4)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.RequestCancellationAsync(At(5), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(5)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2m, order.FilledQuantity.Value);
        Assert.Single(provider.Cancellations);
    }

    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PartiallyFilled)]
    public async Task ExternalEffectiveCancellationAndRedeliveryNeedNoLocalCancelRequest(OrderStatus initial)
    {
        var (order, provider, workflow) = await Submitted();
        if (initial != OrderStatus.Created)
        {
            workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
            Evolve(workflow, initial);
        }

        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(4)));
        Assert.False(workflow.ApplyReport(ExecutionReport.FromCancelled(Id, At(99)), At(-1)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(At(4), order.CompletedAt);
        Assert.Equal(initial == OrderStatus.PartiallyFilled ? 2m : 0m, order.FilledQuantity.Value);
        Assert.Equal(initial == OrderStatus.PartiallyFilled ? 8m : 10m, order.RemainingQuantity.Value);
        Assert.Empty(provider.Cancellations);
    }

    [Theory]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Rejected)]
    public async Task CancellationAfterIncompatibleTerminalFactIsProtocolFailure(OrderStatus status)
    {
        var (order, _, workflow) = await Accepted();
        Evolve(workflow, status);
        var last = order.LastUpdatedAt;
        AssertProtocolFailure(() => workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(4)));
        Assert.Equal(status, order.Status);
        Assert.Equal(last, order.CompletedAt);
    }

    [Fact]
    public async Task FinalFillWhileCancelPendingWinsAndLaterCancelledIsAConflict()
    {
        var (order, provider, workflow) = await Accepted();
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(3));
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.CancelResult = () => source.Task;
        var cancel = workflow.RequestCancellationAsync(At(4), TestContext.Current.CancellationToken);
        workflow.ApplyReport(ExecutionReport.FromFill(Fill(2, 8m), At(90)), At(5));
        AssertProtocolFailure(() => workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(6)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Equal(At(5), order.CompletedAt);
        source.SetException(Failure(ExecutionProviderFailureKind.CancellationRefused, false));
        await Assert.ThrowsAsync<ExecutionProviderException>(() => cancel);
    }

    [Fact]
    public async Task ExplicitApplicationTimeRemainsIndependentAndEqualTransitionTimesAreAllowed()
    {
        var (order, _, workflow) = await Submitted();
        workflow.ApplyReport(ExecutionReport.FromAccepted(Id, At(90)), At(2));
        var fill = Fill(1, 2m);
        workflow.ApplyReport(ExecutionReport.FromFill(fill, At(99)), At(2));
        Assert.Equal(At(2), order.LastUpdatedAt);
        Assert.Equal(At(-10), Assert.Single(order.Fills).ExecutedAt);
        workflow.ApplyReport(ExecutionReport.FromCancelled(Id, At(-90)), At(2));
        Assert.Equal(At(2), order.CompletedAt);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    [InlineData(ExecutionReportKind.Fill)]
    public async Task RegressiveEffectiveTimePreservesDomainGuardAndFailsClosed(ExecutionReportKind kind)
    {
        var (order, _, workflow) = await Submitted();
        if (kind == ExecutionReportKind.Fill)
        {
            workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
        }

        var status = order.Status;
        var last = order.LastUpdatedAt;
        Assert.Throws<DomainException>(() => workflow.ApplyReport(Report(kind), At(-1)));
        Assert.Equal(status, order.Status);
        Assert.Equal(last, order.LastUpdatedAt);
        Assert.Empty(order.Fills);
        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(kind), At(3)));
    }

    [Fact]
    public async Task OperationCancellationForwardsTokenDoesNotCancelOrderAndAllowsSafeExplicitRetry()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var provider = new ScriptedProvider { SubmitResult = () => Task.FromCanceled(source.Token) };
        var order = CreateOrder();
        var workflow = new OrderExecutionWorkflow(order, provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.SubmitOrderAsync(At(1), source.Token));
        Assert.Equal(source.Token, Assert.Single(provider.Submissions).Token);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Empty(provider.Cancellations);
        provider.SubmitResult = () => Task.CompletedTask;
        await workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken);
        workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(3));
        provider.CancelResult = () => Task.FromCanceled(source.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.RequestCancellationAsync(At(4), source.Token));
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(source.Token, Assert.Single(provider.Cancellations).Token);
        provider.CancelResult = () => Task.CompletedTask;
        await workflow.RequestCancellationAsync(At(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Cancellations.Count);
        Assert.Equal(0, provider.ReaderCalls);
    }

    [Fact]
    public async Task UnexpectedUnnormalizedCommandErrorIsPreservedAndNeverAssumedSafeForRetry()
    {
        var error = new InvalidOperationException("Scripted operational failure.");
        var provider = new ScriptedProvider { SubmitResult = () => Task.FromException(error) };
        var workflow = new OrderExecutionWorkflow(CreateOrder(), provider);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidOperationException>(() => { _ = workflow.SubmitOrderAsync(At(2), TestContext.Current.CancellationToken); });
        Assert.True(workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(3)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullProviderCommandTaskIsAContractFailureThatStopsContinuation(bool cancel)
    {
        var (order, provider, workflow) = await Accepted();
        if (cancel)
        {
            provider.CancelResult = () => null!;
            var error = await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken));
            Assert.Equal(ExecutionProviderFailureKind.InvalidProviderResponse, error.FailureKind);
            Assert.Equal(OrderStatus.Accepted, order.Status);
        }
        else
        {
            order = CreateOrder();
            provider = new ScriptedProvider { SubmitResult = () => null! };
            workflow = new OrderExecutionWorkflow(order, provider);
            var error = await Assert.ThrowsAsync<ExecutionProviderException>(() => workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken));
            Assert.Equal(ExecutionProviderFailureKind.InvalidProviderResponse, error.FailureKind);
            Assert.Equal(OrderStatus.Created, order.Status);
        }

        Assert.Throws<InvalidOperationException>(() => workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(4)));
    }

    [Fact]
    public async Task InvalidCancellationArgumentDoesNotDispatchOrConsumeTheAttempt()
    {
        var (_, provider, workflow) = await Accepted();
        Assert.Throws<ArgumentNullException>(() => { _ = workflow.RequestCancellationAsync(null!, TestContext.Current.CancellationToken); });
        Assert.Empty(provider.Cancellations);
        await workflow.RequestCancellationAsync(At(3), TestContext.Current.CancellationToken);
        Assert.Single(provider.Cancellations);
    }

    [Fact]
    public async Task RepeatedSerialScriptProducesIdenticalOrderStateWithoutWallClockOrReader()
    {
        static async Task<Order> Run()
        {
            var (order, provider, workflow) = await Accepted();
            workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(3));
            workflow.ApplyReport(ExecutionReport.FromAccepted(Id, At(99)), At(3));
            await workflow.RequestCancellationAsync(At(4), TestContext.Current.CancellationToken);
            workflow.ApplyReport(ExecutionReport.FromFill(Fill(2, 3m), At(90)), At(5));
            workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(6));
            workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(99)), At(-1));
            workflow.ApplyReport(ExecutionReport.FromCancelled(Id, At(99)), At(-1));
            Assert.Equal(0, provider.ReaderCalls);
            return order;
        }

        var first = await Run();
        var second = await Run();
        Assert.NotSame(first, second);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.FilledQuantity, second.FilledQuantity);
        Assert.Equal(first.RemainingQuantity, second.RemainingQuantity);
        Assert.Equal(first.LastUpdatedAt, second.LastUpdatedAt);
        Assert.Equal(first.CompletedAt, second.CompletedAt);
        Assert.Equal(first.Fills.ToArray(), second.Fills.ToArray());
    }

    private static UtcInstant At(int seconds) => new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds));

    private static Order CreateOrder(OrderSide side = OrderSide.Buy, OrderType type = OrderType.Market)
        => type == OrderType.Market
            ? Order.CreateMarket(Id, Instrument, side, new Quantity(10m), TimeInForce.GTC, At(0))
            : Order.CreateLimit(Id, Instrument, side, new Quantity(10m), new Price(100m), TimeInForce.GTC, At(0));

    private static OrderFill Fill(int index, decimal quantity, decimal price = 100m) => new(
        new ExecutionId(new Guid(index, 0, 0, new byte[8])), Id, new Quantity(quantity), new Price(price), At(-10));

    private static ExecutionReport Report(ExecutionReportKind kind) => kind switch
    {
        ExecutionReportKind.Accepted => ExecutionReport.FromAccepted(Id, At(90)),
        ExecutionReportKind.Rejected => ExecutionReport.FromRejected(Id, At(90)),
        ExecutionReportKind.Cancelled => ExecutionReport.FromCancelled(Id, At(90)),
        ExecutionReportKind.Fill => ExecutionReport.FromFill(Fill(1, 1m), At(90)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ExecutionProviderException Failure(ExecutionProviderFailureKind kind, bool uncertain)
        => new(kind, "scripted", Id, uncertain, "Scripted normalized failure.");

    private static ExecutionProviderException AssertProtocolFailure(Action action)
    {
        var error = Assert.Throws<ExecutionProviderException>(action);
        Assert.Equal(ExecutionProviderFailureKind.InvalidProviderResponse, error.FailureKind);
        Assert.Equal("scripted", error.ProviderKey);
        Assert.True(error.OutcomeUncertain);
        return error;
    }

    private static async Task<(Order Order, ScriptedProvider Provider, OrderExecutionWorkflow Workflow)> Submitted()
    {
        var order = CreateOrder();
        var provider = new ScriptedProvider();
        var workflow = new OrderExecutionWorkflow(order, provider);
        await workflow.SubmitOrderAsync(At(1), TestContext.Current.CancellationToken);
        return (order, provider, workflow);
    }

    private static async Task<(Order Order, ScriptedProvider Provider, OrderExecutionWorkflow Workflow)> Accepted()
    {
        var state = await Submitted();
        state.Workflow.ApplyReport(Report(ExecutionReportKind.Accepted), At(2));
        return state;
    }

    private static void Evolve(OrderExecutionWorkflow workflow, OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.Accepted:
                break;
            case OrderStatus.PartiallyFilled:
                workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 2m), At(90)), At(3));
                break;
            case OrderStatus.Filled:
                workflow.ApplyReport(ExecutionReport.FromFill(Fill(1, 10m), At(90)), At(3));
                break;
            case OrderStatus.Cancelled:
                workflow.ApplyReport(Report(ExecutionReportKind.Cancelled), At(3));
                break;
            case OrderStatus.Rejected:
                workflow.ApplyReport(Report(ExecutionReportKind.Rejected), At(3));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static Order OrderAt(OrderStatus status)
    {
        var order = CreateOrder();
        if (status == OrderStatus.Rejected)
        {
            order.Reject(OrderRejectionReason.PreTradeValidationFailed, At(1));
        }
        else if (status == OrderStatus.Cancelled)
        {
            order.Cancel(At(1));
        }
        else
        {
            order.Accept(At(1));
            if (status is OrderStatus.PartiallyFilled or OrderStatus.Filled)
            {
                order.ApplyFill(Fill(1, status == OrderStatus.Filled ? 10m : 2m), At(2));
            }
        }

        return order;
    }

    private sealed class ScriptedProvider : IExecutionProvider
    {
        public string ProviderKey { get; init; } = "scripted";
        public Func<Task> SubmitResult { get; set; } = () => Task.CompletedTask;
        public Func<Task> CancelResult { get; set; } = () => Task.CompletedTask;
        public List<(OrderSubmission Submission, UtcInstant At, CancellationToken Token)> Submissions { get; } = [];
        public List<(OrderId Id, UtcInstant At, CancellationToken Token)> Cancellations { get; } = [];
        public int ReaderCalls { get; private set; }

        public Task SubmitAsync(OrderSubmission submission, UtcInstant submittedAt, CancellationToken cancellationToken = default)
        {
            Submissions.Add((submission, submittedAt, cancellationToken));
            return SubmitResult();
        }

        public Task RequestCancelAsync(OrderId orderId, UtcInstant requestedAt, CancellationToken cancellationToken = default)
        {
            Cancellations.Add((orderId, requestedAt, cancellationToken));
            return CancelResult();
        }

        public IAsyncEnumerable<ExecutionReport> ReadReportsAsync(CancellationToken cancellationToken = default)
        {
            ReaderCalls++;
            throw new InvalidOperationException("Only the caller may own a report reader.");
        }
    }
}
