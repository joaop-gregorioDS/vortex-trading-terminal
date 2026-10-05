using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Accepts immutable execution commands and supplies normalized facts to Application.</summary>
/// <remarks>
/// One provider instance belongs to one execution scope with one logical report consumer started
/// before commands. Application owns serial Order access and all lifecycle transitions. Providers
/// normalize a consistent per-Order lifecycle sequence; cross-Order ordering has no lifecycle meaning.
/// Duplicates may occur. Economic fill redelivery must preserve ExecutionId and canonical fill content;
/// Application handles duplicate non-fill transitions. No exactly-once delivery, durability or
/// reconnection is promised. External identifiers and raw protocol messages remain adapter-side.
/// Operational failure never implies business rejection or effective cancellation. Explicit command
/// times are supplied by the caller; historical implementations must not consult a wall clock.
/// </remarks>
public interface IExecutionProvider
{
    /// <summary>Stable, non-null, non-blank, trimmed machine identifier within the execution scope.</summary>
    string ProviderKey { get; }

    /// <summary>Submits one immutable intent, correlated exclusively by its OrderId.</summary>
    /// <remarks>
    /// submittedAt is submission time, distinct from CreatedAt, and must not precede CreatedAt.
    /// Normal Task completion means initial operational completion with the corresponding Accepted
    /// or Rejected fact already made available through ReadReportsAsync. It does not mean acceptance,
    /// fill or Order completion, and supplies no second lifecycle result. Equivalent resubmission
    /// represents the same intent; conflicting content for one OrderId must fail without resending.
    /// After an uncertain outcome, callers must reconcile before retrying. The report consumer must
    /// continue independently while the command is pending. Cancellation after possible dispatch must
    /// preserve uncertainty rather than assert that no external effect occurred.
    /// </remarks>
    /// <exception cref="ArgumentException">The input or submission time is structurally invalid.</exception>
    /// <exception cref="ExecutionProviderException">The operation fails with normalized classification and uncertainty.</exception>
    /// <exception cref="OperationCanceledException">The command is canceled before dispatch with no external effect.</exception>
    Task SubmitAsync(OrderSubmission submission, UtcInstant submittedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation without mutating the Order or asserting cancellation on send.</summary>
    /// <remarks>
    /// requestedAt is caller-supplied request time. Normal completion means confirmed effective
    /// cancellation has been made available as a Cancelled report; only Application may apply it.
    /// Intermediate acknowledgment is insufficient. Explicit refusal is CancellationRefused, while
    /// timeout/transport interruption can leave an uncertain outcome. Neither creates a Cancelled
    /// report. Application permits one pending cancellation request per Order and must resolve an
    /// uncertain attempt before retrying. Reports, including fills, remain relevant while pending.
    /// </remarks>
    /// <exception cref="ArgumentException">The Order ID or request time is structurally invalid.</exception>
    /// <exception cref="ExecutionProviderException">Cancellation is refused or fails operationally.</exception>
    /// <exception cref="OperationCanceledException">The command is canceled before dispatch with no external effect.</exception>
    Task RequestCancelAsync(OrderId orderId, UtcInstant requestedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Reads normalized facts for the execution scope through one logical consumer.</summary>
    /// <remarks>
    /// Orders are correlated by OrderId. New fill facts follow normalized acceptance; initial rejection
    /// need not follow acceptance. Cancelled denotes confirmed effective cancellation. Duplicate facts
    /// may be redelivered without exactly-once guarantees. Out-of-order external messages are a future
    /// adapter normalization responsibility; terminal conflicts need reconciliation, not weaker Order
    /// guards. Canceling this stream only stops consumption and does not cancel external Orders.
    /// </remarks>
    /// <exception cref="ExecutionProviderException">The stream fails operationally; no Order rejection is implied.</exception>
    /// <exception cref="OperationCanceledException">Report consumption is canceled.</exception>
    IAsyncEnumerable<ExecutionReport> ReadReportsAsync(CancellationToken cancellationToken = default);
}
