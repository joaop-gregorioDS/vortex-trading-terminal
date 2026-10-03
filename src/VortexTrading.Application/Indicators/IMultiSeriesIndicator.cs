using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Indicators;

/// <summary>Deterministic batch calculation producing multiple named indicator series.</summary>
public interface IMultiSeriesIndicator
{
    string Name { get; }

    MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles);
}
