namespace VortexTrading.Application.Indicators;

/// <summary>An immutable name/series pair; series equality retains IndicatorSeries reference semantics.</summary>
public sealed record NamedIndicatorSeries
{
    public string Name { get; }
    public IndicatorSeries Series { get; }

    public NamedIndicatorSeries(string? name, IndicatorSeries? series)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Indicator series name must not be blank.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(series);
        Name = name.Trim();
        Series = series;
    }
}
