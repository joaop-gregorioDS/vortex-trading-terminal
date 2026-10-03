namespace VortexTrading.Application.Indicators;

/// <summary>An immutable snapshot preserving supplied point order and occurrences.</summary>
public sealed class IndicatorSeries
{
    public IReadOnlyList<IndicatorPoint> Points { get; }
    public int Count => Points.Count;

    public IndicatorSeries(IReadOnlyCollection<IndicatorPoint>? points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var snapshot = points.ToArray();
        if (snapshot.Any(point => point is null))
        {
            throw new ArgumentException("Indicator series must not contain null points.", nameof(points));
        }

        Points = Array.AsReadOnly(snapshot);
    }
}
