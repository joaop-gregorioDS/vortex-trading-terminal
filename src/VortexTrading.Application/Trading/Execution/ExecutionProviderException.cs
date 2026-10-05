using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Exposes a sanitized operational failure without implying any Order lifecycle transition.</summary>
/// <remarks>
/// The producer supplies sanitized text and explicit outcome uncertainty. ProviderKey is trimmed
/// without changing case. OrderId may be absent for session failures. No inner-exception constructor
/// is exposed: SDK exceptions and raw diagnostics must remain outside this normalized boundary.
/// Classification never manufactures Rejected or Cancelled reports.
/// </remarks>
public sealed class ExecutionProviderException : Exception
{
    public ExecutionProviderFailureKind FailureKind { get; }
    public string ProviderKey { get; }
    public OrderId? OrderId { get; }
    public bool OutcomeUncertain { get; }

    public ExecutionProviderException(ExecutionProviderFailureKind failureKind, string? providerKey,
        OrderId? orderId, bool outcomeUncertain, string? message) : base(message)
    {
        if (!Enum.IsDefined(failureKind))
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        FailureKind = failureKind;
        ProviderKey = providerKey.Trim();
        OrderId = orderId;
        OutcomeUncertain = outcomeUncertain;
    }
}
