using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>One price the simulated feed published for an instrument.</summary>
public sealed record PriceObservation(UtcInstant EventTime, decimal ReferencePrice, decimal Volume);

/// <summary>What the matcher currently offers for one instrument, plus the operator's resting orders.</summary>
public sealed record InstrumentBookView(
    Price? LastPrice,
    decimal AvailableVolume,
    IReadOnlyList<PriceObservation> Observations,
    IReadOnlyList<Order> RestingOrders);