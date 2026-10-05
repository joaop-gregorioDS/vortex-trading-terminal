namespace VortexTrading.Application.Trading.Execution;

/// <summary>Classifies operational failures, independently of Order business rejection.</summary>
public enum ExecutionProviderFailureKind
{
    Unavailable,
    TransportFailure,
    Timeout,
    InvalidProviderResponse,
    AdapterFailure,
    CancellationRefused,
    OutcomeUnknown
}
