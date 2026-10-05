namespace VortexTrading.Application.Trading.Execution;

/// <summary>Identifies a confirmed, normalized execution-provider lifecycle fact.</summary>
public enum ExecutionReportKind
{
    Accepted,
    Rejected,
    Fill,
    Cancelled
}
