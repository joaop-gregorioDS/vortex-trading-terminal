using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>One price the simulated session published, kept for charting.</summary>
/// <remarks>
/// Holds the exact decimal the session traded against, not a floating point copy, so a chart drawn
/// from it cannot disagree with the price an execution used.
/// </remarks>
public sealed record PriceObservation(UtcInstant EventTime, decimal ReferencePrice, decimal Volume);
