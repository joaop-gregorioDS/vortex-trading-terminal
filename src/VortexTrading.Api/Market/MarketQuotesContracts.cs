namespace VortexTrading.Api.Market;

public sealed record MarketQuoteResponse(
    string Ticker,
    string Name,
    string Category,
    string InstrumentId,
    string LastPrice,
    string ChangePercent,
    string ChangePoints,
    string Min,
    string Max,
    string Volume,
    string UpdatedAt);

public sealed record RestingOrderResponse(
    string OrderId,
    string Side,
    string Type,
    string? LimitPrice,
    string RemainingQuantity,
    string Status);

/// <summary>The price and leftover volume the matcher is offering, plus the operator's resting orders.</summary>
public sealed record MarketDepthResponse(
    string Ticker,
    string InstrumentId,
    string LastPrice,
    string AvailableVolume,
    string TickSize,
    IReadOnlyList<RestingOrderResponse> RestingOrders);
