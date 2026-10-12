using System.Text.Json.Serialization;

namespace VortexTrading.Api.Charts;

public sealed record ChartSnapshotRequest
{
    [JsonRequired] public Guid InstrumentId { get; init; }
    [JsonRequired] public required string Interval { get; init; }
    public int MaxBars { get; init; } = 200;
    [JsonRequired] public required IReadOnlyList<IndicatorRequest> Indicators { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "id")]
[JsonDerivedType(typeof(SmaRequest), "sma")]
[JsonDerivedType(typeof(MacdRequest), "macd")]
[JsonDerivedType(typeof(BollingerBandsRequest), "bollinger-bands")]
[JsonDerivedType(typeof(RsiRequest), "rsi")]
[JsonDerivedType(typeof(VwapRequest), "vwap")]
public abstract record IndicatorRequest;

public sealed record SmaRequest : IndicatorRequest
{
    [JsonRequired] public required SmaParameters Parameters { get; init; }
}

public sealed record MacdRequest : IndicatorRequest
{
    [JsonRequired] public required MacdParameters Parameters { get; init; }
}

public sealed record BollingerBandsRequest : IndicatorRequest
{
    [JsonRequired] public required BollingerBandsParameters Parameters { get; init; }
}

public sealed record RsiRequest : IndicatorRequest
{
    [JsonRequired] public required RsiParameters Parameters { get; init; }
}

public sealed record VwapRequest : IndicatorRequest
{
    public VwapParameters? Parameters { get; init; }
}

[JsonDerivedType(typeof(SmaParameters))]
[JsonDerivedType(typeof(MacdParameters))]
[JsonDerivedType(typeof(BollingerBandsParameters))]
[JsonDerivedType(typeof(RsiParameters))]
[JsonDerivedType(typeof(VwapParameters))]
public abstract record IndicatorParameters;

public sealed record SmaParameters : IndicatorParameters
{
    [JsonRequired] public int Period { get; init; }
}

public sealed record MacdParameters : IndicatorParameters
{
    [JsonRequired] public int FastPeriod { get; init; }
    [JsonRequired] public int SlowPeriod { get; init; }
    [JsonRequired] public int SignalPeriod { get; init; }
}

public sealed record BollingerBandsParameters : IndicatorParameters
{
    public int Period { get; init; } = 20;
    public decimal Multiplier { get; init; } = 2.0m;
}

public sealed record RsiParameters : IndicatorParameters
{
    public int Period { get; init; } = 14;
}

public sealed record VwapParameters : IndicatorParameters;

public sealed record ChartContextResponse(string InstrumentId, string Interval, string SourceId, bool IsDemo, int BarCount);
public sealed record CandleResponse(string OpenTime, string CloseTime, string Open, string High, string Low, string Close, string Volume);
public sealed record PointResponse(string Time, string Value);
public sealed record SeriesResponse(string Name, IReadOnlyList<PointResponse> Points);
public sealed record IndicatorResponse(string Id, IndicatorParameters Parameters, IReadOnlyList<SeriesResponse> Series);
public sealed record ChartSnapshotResponse(ChartContextResponse Context,
    IReadOnlyList<CandleResponse> Candles, IReadOnlyList<IndicatorResponse> Indicators);
