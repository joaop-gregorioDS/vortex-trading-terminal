using System.Globalization;
using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Api.Charts;

public static class ChartSnapshotMapping
{
    public static HistoricalChartRequest ToApplication(ChartSnapshotRequest request)
    {
        if (request.InstrumentId == Guid.Empty || string.IsNullOrEmpty(request.Interval)
            || request.Indicators is null || request.Indicators.Any(item => item is null))
            throw new ChartTransportException();

        // Tokens have explicit meaning; no fallback to the demo interval or instrument.
        var interval = request.Interval switch
        {
            "1m" => CandleInterval.OneMinute,
            "5m" => CandleInterval.FiveMinutes,
            _ => throw new ChartTransportException()
        };
        var specifications = request.Indicators.Select<IndicatorRequest, IndicatorSpecification>(item => item switch
        {
            SmaRequest { Parameters: not null } s => new SmaSpecification(s.Parameters.Period),
            MacdRequest { Parameters: not null } m => new MacdSpecification(
                m.Parameters.FastPeriod, m.Parameters.SlowPeriod, m.Parameters.SignalPeriod),
            _ => throw new ChartTransportException()
        }).ToArray();
        return new HistoricalChartRequest(new InstrumentId(request.InstrumentId), interval,
            request.MaxBars, Array.AsReadOnly(specifications));
    }

    public static ChartSnapshotResponse ToResponse(HistoricalChartSnapshot snapshot) => new(
        new ChartContextResponse(snapshot.Context.InstrumentId.ToString(), Interval(snapshot.Context.Interval), snapshot.Context.SourceId,
            snapshot.Context.IsDemo, snapshot.Candles.Count),
        snapshot.Candles.Select(candle => new CandleResponse(Time(candle.OpenTime), Time(candle.CloseTime),
            Decimal(candle.Open.Value), Decimal(candle.High.Value), Decimal(candle.Low.Value),
            Decimal(candle.Close.Value), Decimal(candle.Volume.Value))).ToArray(),
        snapshot.Indicators.Select(indicator => new IndicatorResponse(indicator.Specification.MachineId,
            Parameters(indicator.Specification), indicator.Result.Series.Select(series => new SeriesResponse(
                series.Name, series.Series.Points.Select(point => new PointResponse(Time(point.Time), Decimal(point.Value)))
                    .ToArray())).ToArray())).ToArray());

    private static IndicatorParameters Parameters(IndicatorSpecification specification) => specification switch
    {
        SmaSpecification s => new SmaParameters { Period = s.Period },
        MacdSpecification m => new MacdParameters
        {
            FastPeriod = m.FastPeriod, SlowPeriod = m.SlowPeriod, SignalPeriod = m.SignalPeriod
        },
        _ => throw new InvalidOperationException("Unsupported server chart result.")
    };

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Interval(CandleInterval interval) => interval == CandleInterval.OneMinute ? "1m"
        : interval == CandleInterval.FiveMinutes ? "5m" : throw new InvalidOperationException("Unsupported server interval.");
    private static string Time(UtcInstant time) => time.Value.UtcDateTime.ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}

public sealed class ChartTransportException : Exception;
