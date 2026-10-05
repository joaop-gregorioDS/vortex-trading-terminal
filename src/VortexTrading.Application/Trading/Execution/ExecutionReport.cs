using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Contains exactly one normalized provider fact without applying an Order transition.</summary>
/// <remarks>
/// ObservedAt belongs to the normalizer; it is independent of fill occurrence, Application receipt
/// and Order application time. Rejected means confirmed business rejection. Cancelled means effective
/// cancellation, never a pending request. Fill retains the original immutable OrderFill and its ID.
/// </remarks>
public sealed class ExecutionReport
{
    public OrderId OrderId { get; }
    public ExecutionReportKind Kind { get; }
    public UtcInstant ObservedAt { get; }
    public OrderFill? Fill { get; }

    private ExecutionReport(OrderId? orderId, ExecutionReportKind kind, UtcInstant? observedAt)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(observedAt);
        OrderId = orderId;
        Kind = kind;
        ObservedAt = observedAt;
    }

    private ExecutionReport(OrderFill? fill, UtcInstant? observedAt)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(observedAt);
        OrderId = fill.OrderId;
        Kind = ExecutionReportKind.Fill;
        ObservedAt = observedAt;
        Fill = fill;
    }

    public static ExecutionReport FromAccepted(OrderId? orderId, UtcInstant? observedAt)
        => new(orderId, ExecutionReportKind.Accepted, observedAt);

    public static ExecutionReport FromRejected(OrderId? orderId, UtcInstant? observedAt)
        => new(orderId, ExecutionReportKind.Rejected, observedAt);

    public static ExecutionReport FromFill(OrderFill? fill, UtcInstant? observedAt)
        => new(fill, observedAt);

    public static ExecutionReport FromCancelled(OrderId? orderId, UtcInstant? observedAt)
        => new(orderId, ExecutionReportKind.Cancelled, observedAt);
}
