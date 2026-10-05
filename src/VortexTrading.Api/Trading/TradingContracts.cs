using System.Text.Json.Serialization;

namespace VortexTrading.Api.Trading;

/// <summary>Transport shape for submitting one paper order.</summary>
/// <remarks>
/// Exact values travel as decimal strings, following the same rule as the chart transport and
/// ADR-002: a JSON number would be parsed as a binary floating-point value and would stop being
/// authoritative for money.
/// </remarks>
public sealed record OrderRequest
{
    [JsonRequired] public Guid InstrumentId { get; init; }
    [JsonRequired] public required string Side { get; init; }
    [JsonRequired] public required string Type { get; init; }
    [JsonRequired] public required string Quantity { get; init; }
    public string? LimitPrice { get; init; }
    public string? TimeInForce { get; init; }
}

public sealed record FillResponse(string ExecutionId, string Quantity, string Price, string ExecutedAt);

public sealed record OrderResponse(
    string OrderId,
    string InstrumentId,
    string Side,
    string Type,
    string Quantity,
    string? LimitPrice,
    string TimeInForce,
    string Status,
    string FilledQuantity,
    string RemainingQuantity,
    string? RejectionReason,
    string CreatedAt,
    string LastUpdatedAt,
    string? CompletedAt,
    IReadOnlyList<FillResponse> Fills);

public sealed record PositionResponse(
    string InstrumentId,
    string Quantity,
    string TotalCost,
    string AverageCost,
    string RealizedProfitAndLoss,
    string UnrealizedProfitAndLoss,
    string TotalProfitAndLoss,
    bool IsClosed,
    string? UpdatedAt);

public sealed record PortfolioResponse(
    string Cash,
    string Equity,
    string TotalCost,
    string RealizedProfitAndLoss,
    string UnrealizedProfitAndLoss,
    string TotalProfitAndLoss,
    IReadOnlyList<PositionResponse> Positions);

public sealed record MarketResponse(
    string InstrumentId,
    string? LastPrice,
    string? SimulatedTime,
    int RestingOrderCount);
