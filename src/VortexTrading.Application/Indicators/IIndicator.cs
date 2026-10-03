using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>Deterministic batch calculation over explicitly supplied closed candles.</summary>
public interface IIndicator
{
    string Name { get; }

    IndicatorSeries Calculate(IReadOnlyList<Candle> candles);
}
