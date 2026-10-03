namespace VortexTrading.Application.Indicators;

/// <summary>An immutable ordered snapshot of uniquely named series, without cross-series alignment.</summary>
public sealed class MultiSeriesIndicatorResult
{
    public IReadOnlyList<NamedIndicatorSeries> Series { get; }
    public int Count => Series.Count;

    public MultiSeriesIndicatorResult(IEnumerable<NamedIndicatorSeries>? series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var snapshot = series.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in snapshot)
        {
            if (item is null)
            {
                throw new ArgumentException("Indicator result must not contain null series.", nameof(series));
            }

            if (!names.Add(item.Name))
            {
                throw new ArgumentException("Indicator result series names must be unique.", nameof(series));
            }
        }

        Series = Array.AsReadOnly(snapshot);
    }

    public bool TryGetSeries(string? name, out IndicatorSeries? series)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Indicator series lookup name must not be blank.", nameof(name));
        }

        var normalizedName = name.Trim();
        foreach (var item in Series)
        {
            if (!StringComparer.Ordinal.Equals(item.Name, normalizedName)) continue;
            series = item.Series;
            return true;
        }

        series = null;
        return false;
    }
}
