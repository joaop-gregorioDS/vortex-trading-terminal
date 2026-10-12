namespace VortexTrading.Application.Risk;

/// <summary>Configuration settings for pre-trade risk controls.</summary>
public sealed class RiskOptions
{
    public const string SectionName = "VortexTrading:Risk";

    /// <summary>Whether pre-trade risk evaluation is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum quantity allowed for an individual order, or null for no limit.</summary>
    public decimal? MaxOrderQuantity { get; set; } = 1000m;

    /// <summary>Maximum estimated financial notional allowed for an individual order, or null for no limit.</summary>
    public decimal? MaxOrderNotional { get; set; } = 50000m;

    /// <summary>Maximum position quantity (held + pending) allowed in an instrument, or null for no limit.</summary>
    public decimal? MaxPositionQuantity { get; set; } = 2000m;

    /// <summary>Whether market orders require an active reference price to be accepted.</summary>
    public bool RequireReferencePriceForMarketOrders { get; set; } = true;
}
