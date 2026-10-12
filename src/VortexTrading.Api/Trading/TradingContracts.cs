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
    public string? Type { get; init; }
    [JsonRequired] public required string Quantity { get; init; }
    public string? LimitPrice { get; init; }
    public string? TimeInForce { get; init; }

    /// <summary>"chart" rests on later chart closes. Absent or "session" keeps the simulated ticket.</summary>
    public string? Origin { get; init; }

    /// <summary>Optional bracket armed when this order fills. Prices are computed by the server.</summary>
    public ProtectionRequest? Protection { get; init; }
}

/// <summary>Distances for an OCO bracket. Amounts travel as decimal strings. Unit is ticks, points or percent.</summary>
public sealed record ProtectionRequest
{
    public string? Gain { get; init; }
    public string? Loss { get; init; }
    public string? Unit { get; init; }
    public string? Trail { get; init; }
    public string? BreakEven { get; init; }
}

public sealed record InstrumentActionRequest
{
    [JsonRequired] public Guid InstrumentId { get; init; }
}

public sealed record ReviseOrderRequest
{
    [JsonRequired] public required string Price { get; init; }
}

public sealed record ProtectionLevelRequest
{
    [JsonRequired] public Guid InstrumentId { get; init; }
    [JsonRequired] public required string Price { get; init; }
    public string? Origin { get; init; }
}

public sealed record FillResponse(string ExecutionId, string Quantity, string Price, string ExecutedAt);

public sealed record OrderResponse(
    string OrderId,
    string InstrumentId,
    string Side,
    string Type,
    string Origin,
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
    IReadOnlyList<FillResponse> Fills,
    string? Role = null);

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

/// <summary>Operational facts about the running session, for monitoring without reading the log.</summary>
public sealed record StatusResponse(
    string InstrumentId,
    string? SimulatedTime,
    string? LastPrice,
    int RestingOrderCount,
    int OrderCount,
    int FilledOrderCount,
    int OpenOrderCount,
    int RejectedOrderCount,
    int FillCount,
    string TradedQuantity,
    int RestoredOrderCount);
