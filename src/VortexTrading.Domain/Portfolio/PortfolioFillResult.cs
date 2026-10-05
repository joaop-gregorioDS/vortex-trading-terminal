namespace VortexTrading.Domain.Portfolio;

/// <summary>Outcome of applying one fill to a portfolio.</summary>
/// <remarks>
/// Mirrors <see cref="PositionFillResult"/> but carries the whole portfolio, so a caller never has to
/// rebuild it after applying a fill. An excess sell is reported rather than hidden (ADR-038).
/// </remarks>
public sealed record PortfolioFillResult
{
    public PortfolioFillResult(Portfolio? Portfolio, decimal AppliedQuantity, decimal IgnoredQuantity)
    {
        ArgumentNullException.ThrowIfNull(Portfolio);
        this.Portfolio = Portfolio;
        this.AppliedQuantity = AppliedQuantity;
        this.IgnoredQuantity = IgnoredQuantity;
    }

    /// <summary>Portfolio after the fill.</summary>
    public Portfolio Portfolio { get; }

    /// <summary>Quantity the fill actually moved.</summary>
    public decimal AppliedQuantity { get; }

    /// <summary>Quantity the fill wanted to move but could not, because the position was too small.</summary>
    public decimal IgnoredQuantity { get; }

    /// <summary>True when part of the fill could not be applied.</summary>
    public bool HadUnappliedQuantity => IgnoredQuantity != 0m;
}
