namespace VortexTrading.Domain.Trading;

/// <summary>Which price tape an order rests on.</summary>
/// <remarks>
/// Session orders match the simulated prints. Chart orders match later closes of the chart series.
/// A print from one tape never fills an order that rests on the other.
/// </remarks>
public enum OrderOrigin
{
    Session,
    Chart
}
