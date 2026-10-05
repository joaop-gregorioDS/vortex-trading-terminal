namespace VortexTrading.Domain.Portfolio;

/// <summary>Outcome of applying one fill to a position.</summary>
/// <remarks>
/// Carries the resulting position plus what the fill actually did, so a caller can see that an excess
/// sell was ignored instead of inferring it from a quantity difference. Nothing is silently dropped.
/// </remarks>
public sealed record PositionFillResult
{
    public PositionFillResult(Position? Position, decimal AppliedQuantity, decimal IgnoredQuantity)
    {
        ArgumentNullException.ThrowIfNull(Position);
        this.Position = Position;
        this.AppliedQuantity = AppliedQuantity;
        this.IgnoredQuantity = IgnoredQuantity;
    }

    /// <summary>Position after the fill.</summary>
    public Position Position { get; }

    /// <summary>Quantity the fill actually moved. For a buy this equals the fill quantity.</summary>
    public decimal AppliedQuantity { get; }

    /// <summary>Quantity the fill wanted to move but could not, because the position was too small.</summary>
    public decimal IgnoredQuantity { get; }

    /// <summary>True when part of the fill could not be applied.</summary>
    public bool HadUnappliedQuantity => IgnoredQuantity != 0m;
}
