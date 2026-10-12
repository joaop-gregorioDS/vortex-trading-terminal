namespace VortexTrading.Application.Risk;

/// <summary>Outcome of a pre-trade risk evaluation for an order intent.</summary>
public sealed record RiskDecision(bool IsApproved, string? Reason = null)
{
    /// <summary>Creates an approved decision allowing the order to proceed to execution.</summary>
    public static RiskDecision Approve() => new(true);

    /// <summary>Creates a rejected decision with a human-readable explanation.</summary>
    public static RiskDecision Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
}
