using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Owns one Order's execution commands and caller-delivered lifecycle facts.</summary>
/// <remarks>
/// Requires exclusive, serial logical ownership of the Order and all workflow calls; it is not
/// thread-safe. The caller starts and owns the provider scope's single report consumer, keeps it
/// active while command Tasks are pending, supplies application times, and observes every returned
/// Task. No reader, background continuation, clock, recovery or automatic retry belongs here.
/// Do not mutate the Order externally or recreate a workflow to bypass unresolved external effects.
/// </remarks>
public sealed class OrderExecutionWorkflow
{
    private readonly Order _order;
    private readonly IExecutionProvider _provider;
    private readonly string _providerKey;
    private readonly OrderSubmission _submission;
    private Task? _submitTask;
    private Task? _cancelTask;
    private bool _submitResultObserved;
    private bool _cancelResultObserved;
    private bool _submissionAttempted;
    private bool _submissionMayHaveEffect;
    private bool _submissionUncertain;
    private bool _submissionRetryAllowed = true;
    private bool _cancellationAwaitingReport;
    private bool _cancellationUncertain;
    private bool _acceptedApplied;
    private bool _rejectedApplied;
    private bool _cancelledApplied;
    private bool _integrityFailed;

    public OrderExecutionWorkflow(Order order, IExecutionProvider provider, UtcInstant? earliestFillOpen = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(provider);
        if (order.Status != OrderStatus.Created)
        {
            throw new InvalidOperationException("Execution workflow requires a created Order.");
        }

        var key = provider.ProviderKey;
        if (string.IsNullOrWhiteSpace(key) || key != key.Trim())
        {
            throw new ArgumentException("Provider identity must be non-blank and trimmed.", nameof(provider));
        }

        _order = order;
        _provider = provider;
        _providerKey = key;
        _submission = new OrderSubmission(order.Id, order.InstrumentId, order.Side, order.Type,
            order.Quantity, order.LimitPrice, order.TimeInForce, order.CreatedAt, order.Origin,
            earliestFillOpen);
    }

    /// <summary>Submits the retained intent; completion does not apply the initial business report.</summary>
    /// <remarks>Only an explicit retry after proven absence of external effect is permitted.</remarks>
    public Task SubmitOrderAsync(UtcInstant submittedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submittedAt);
        EnsureHealthy();
        ObserveCompletedCommands();
        if (submittedAt.CompareTo(_order.CreatedAt) < 0)
        {
            throw new ArgumentException("Submission time must not precede creation.", nameof(submittedAt));
        }

        if (_order.Status != OrderStatus.Created || _submitTask is { IsCompleted: false }
            || !_submissionRetryAllowed || _submissionUncertain || HasCommercialFact)
        {
            throw new InvalidOperationException("Order submission is pending, resolved or unsafe to retry.");
        }

        // Record before calling the provider, including providers which throw synchronously.
        _submissionAttempted = true;
        _submissionMayHaveEffect = true;
        _submissionRetryAllowed = false;
        _submitResultObserved = false;
        try
        {
            _submitTask = _provider.SubmitAsync(_submission, submittedAt, cancellationToken)
                ?? throw ProtocolFailure("Provider returned no submission Task.");
        }
        catch (Exception error)
        {
            _submitTask = Task.FromException(error);
        }

        return _submitTask;
    }

    /// <summary>Requests effective cancellation; only a Cancelled report changes the Order.</summary>
    public Task RequestCancellationAsync(UtcInstant requestedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAt);
        EnsureHealthy();
        ObserveCompletedCommands();
        if (_order.Status is not (OrderStatus.Accepted or OrderStatus.PartiallyFilled)
            || _cancelTask is { IsCompleted: false } || _cancellationAwaitingReport
            || _cancellationUncertain || _submissionUncertain)
        {
            throw new InvalidOperationException("Order is not eligible for a new cancellation request.");
        }

        _cancellationAwaitingReport = true;
        _cancelResultObserved = false;
        try
        {
            _cancelTask = _provider.RequestCancelAsync(_order.Id, requestedAt, cancellationToken)
                ?? throw ProtocolFailure("Provider returned no cancellation Task.");
        }
        catch (Exception error)
        {
            _cancelTask = Task.FromException(error);
        }

        return _cancelTask;
    }

    /// <summary>Applies one correlated fact at an explicit caller time; false means equivalent redelivery.</summary>
    /// <remarks>ObservedAt and ExecutedAt are never substituted for at. Integrity failures stop continuation.</remarks>
    public bool ApplyReport(ExecutionReport report, UtcInstant at)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(at);
        EnsureHealthy();
        ObserveCompletedCommands();
        if (report.OrderId != _order.Id)
        {
            throw ProtocolFailure("Execution report targets a different Order.");
        }

        if (!_submissionAttempted || !(_submissionMayHaveEffect || HasCommercialFact))
        {
            throw ProtocolFailure("Execution report has no submission with possible external effect.");
        }

        try
        {
            switch (report.Kind)
            {
                case ExecutionReportKind.Accepted:
                    if (_acceptedApplied)
                    {
                        return false;
                    }

                    if (_order.Status != OrderStatus.Created)
                    {
                        throw ProtocolFailure("Acceptance conflicts with the known Order lifecycle.");
                    }

                    _order.Accept(at);
                    _acceptedApplied = true;
                    _submissionUncertain = false;
                    return true;

                case ExecutionReportKind.Rejected:
                    if (_rejectedApplied)
                    {
                        return false;
                    }

                    if (_order.Status != OrderStatus.Created
                        && !(_order.Status == OrderStatus.Accepted && _order.Fills.Count == 0))
                    {
                        throw ProtocolFailure("Provider rejection conflicts with the known Order lifecycle.");
                    }

                    _order.Reject(OrderRejectionReason.ExecutionProviderRejected, at);
                    _rejectedApplied = true;
                    _submissionUncertain = false;
                    return true;

                case ExecutionReportKind.Fill:
                    // Domain alone handles identity, equivalent terminal duplicates and economic guards.
                    return _order.ApplyFill(report.Fill, at);

                case ExecutionReportKind.Cancelled:
                    if (_cancelledApplied)
                    {
                        return false;
                    }

                    if (_order.Status is not (OrderStatus.Created or OrderStatus.Accepted or OrderStatus.PartiallyFilled))
                    {
                        throw ProtocolFailure("Effective cancellation conflicts with the known Order lifecycle.");
                    }

                    _order.Cancel(at);
                    _cancelledApplied = true;
                    _submissionUncertain = false;
                    _cancellationAwaitingReport = false;
                    _cancellationUncertain = false;
                    return true;

                default:
                    throw ProtocolFailure("Execution report has an unsupported kind.");
            }
        }
        catch (DomainException)
        {
            // Preserve the original guard failure and its atomicity; no message parsing or repair.
            _integrityFailed = true;
            throw;
        }
    }

    private bool HasCommercialFact => _acceptedApplied || _rejectedApplied || _cancelledApplied;

    private void ObserveCompletedCommands()
    {
        // Only serial public entries reconcile completed Tasks. Nothing mutates state on completion
        // in the background, and GetResult is used only after IsCompleted (never a blocking wait).
        if (!_submitResultObserved && _submitTask is { IsCompleted: true })
        {
            _submitResultObserved = true;
            try
            {
                _submitTask.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                var noEffect = DefinitelyNoExternalEffect(error);
                _submissionMayHaveEffect = !noEffect || HasCommercialFact;
                _submissionUncertain = !noEffect && !HasCommercialFact;
                _submissionRetryAllowed = noEffect && !HasCommercialFact;
            }
        }

        if (!_cancelResultObserved && _cancelTask is { IsCompleted: true })
        {
            _cancelResultObserved = true;
            try
            {
                _cancelTask.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                _cancellationUncertain = !DefinitelyNoExternalEffect(error) && !_cancelledApplied;
                _cancellationAwaitingReport = _cancellationUncertain;
            }
        }
    }

    private static bool DefinitelyNoExternalEffect(Exception error) => error is OperationCanceledException
        || error is ExecutionProviderException { OutcomeUncertain: false };

    private void EnsureHealthy()
    {
        if (_integrityFailed)
        {
            throw new InvalidOperationException("Execution workflow integrity failed; reconciliation is required.");
        }
    }

    private ExecutionProviderException ProtocolFailure(string message)
    {
        _integrityFailed = true;
        return new ExecutionProviderException(ExecutionProviderFailureKind.InvalidProviderResponse,
            _providerKey, _order.Id, true, message);
    }
}
