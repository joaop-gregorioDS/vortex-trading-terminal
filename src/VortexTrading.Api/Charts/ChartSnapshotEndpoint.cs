using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VortexTrading.Application.Charts;

namespace VortexTrading.Api.Charts;

public static class ChartSnapshotEndpoint
{
    public const int MaximumBodyBytes = 8192;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        AllowOutOfOrderMetadataProperties = true
    };

    public static void MapHistoricalChartSnapshots(this WebApplication app) =>
        app.MapPost("/api/chart-snapshots", HandleAsync);

    public static async Task<IResult> HandleAsync(HttpContext context, HistoricalChartQuery query, ILoggerFactory loggerFactory)
    {
        var timer = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("HistoricalChartSnapshot");
        ChartSnapshotRequest? request = null;
        var barCount = 0;
        var status = 500;
        var category = "internal_error";
        IResult Failure(int code, string error, string title)
        {
            status = code;
            category = error;
            return Results.Problem(statusCode: code, title: title, extensions: new Dictionary<string, object?>
            {
                ["code"] = error, ["traceId"] = context.TraceIdentifier
            });
        }

        try
        {
            if (context.Request.ContentLength > MaximumBodyBytes)
                return Failure(413, "body_too_large", "Request body exceeds the chart limit.");
            var bytes = new byte[MaximumBodyBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBodyBytes)
                return Failure(413, "body_too_large", "Request body exceeds the chart limit.");
            request = JsonSerializer.Deserialize<ChartSnapshotRequest>(bytes.AsSpan(0, count), JsonOptions);
            if (request is null) throw new ChartTransportException();
            var snapshot = await query.ExecuteAsync(ChartSnapshotMapping.ToApplication(request), context.RequestAborted);
            barCount = snapshot.Candles.Count;
            status = 200;
            category = "success";
            return Results.Json(ChartSnapshotMapping.ToResponse(snapshot), JsonOptions);
        }
        catch (JsonException)
        {
            return Failure(400, "invalid_request", "Invalid chart request or unsupported indicator shape.");
        }
        catch (ChartTransportException)
        {
            return Failure(400, "invalid_request", "Invalid chart request or unsupported indicator shape.");
        }
        catch (HistoricalChartException exception)
        {
            return exception.Failure switch
            {
                HistoricalChartFailure.ResourceLimit => Failure(400, "resource_limit", "Chart resource limit exceeded."),
                HistoricalChartFailure.ContextNotFound => Failure(404, "context_not_found", "Historical context was not found."),
                HistoricalChartFailure.InvalidConfiguration => Failure(422, "invalid_configuration", "Invalid indicator configuration."),
                HistoricalChartFailure.CalculationUnavailable => Failure(422, "calculation_unavailable", "Indicator calculation is unavailable."),
                HistoricalChartFailure.SourceUnavailable => Failure(503, "source_unavailable", "Historical source is unavailable."),
                _ => Failure(500, "internal_error", "Chart snapshot could not be produced.")
            };
        }
        finally
        {
            logger.LogInformation(
                "Chart snapshot {InstrumentId} {Interval} bars {BarCount} indicators {IndicatorCount} duration {DurationMs} status {Status} category {Category} trace {TraceId}",
                request?.InstrumentId, request?.Interval is "1m" or "5m" ? request.Interval : "unsupported",
                barCount, request?.Indicators?.Count ?? 0, timer.ElapsedMilliseconds, status, category, context.TraceIdentifier);
        }
    }
}
