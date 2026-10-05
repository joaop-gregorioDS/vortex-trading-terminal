using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Paper;

/// <summary>
/// One market fact available to paper matching: a reference price and the volume that can be
/// executed against it.
/// </summary>
/// <remarks>
/// This is the only price and liquidity input of the paper provider. The caller resolves it — from a
/// closed candle, a trade or any other authorized source — which keeps price resolution explicit and
/// auditable instead of hidden inside the provider (ADR-036).
/// <para>
/// <see cref="EventTime"/> is when the price became known. An order only matches observations whose
/// time is strictly later than its own submission, so a price that existed before the intent can
/// never fill it. <see cref="AvailableVolume"/> is consumed by fills in arrival order and is finite
/// on purpose: it is what makes partial fills reachable.
/// </para>
/// </remarks>
public sealed class PaperMarketObservation
{
    public UtcInstant EventTime { get; }
    public InstrumentId InstrumentId { get; }
    public Price ReferencePrice { get; }
    public Quantity AvailableVolume { get; }

    public PaperMarketObservation(UtcInstant? eventTime, InstrumentId? instrumentId,
        Price? referencePrice, Quantity? availableVolume)
    {
        ArgumentNullException.ThrowIfNull(eventTime);
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(referencePrice);
        ArgumentNullException.ThrowIfNull(availableVolume);

        if (referencePrice.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(referencePrice),
                "Reference price must be positive.");
        }

        if (availableVolume.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableVolume),
                "Available volume must be positive.");
        }

        EventTime = eventTime;
        InstrumentId = instrumentId;
        ReferencePrice = referencePrice;
        AvailableVolume = availableVolume;
    }
}
