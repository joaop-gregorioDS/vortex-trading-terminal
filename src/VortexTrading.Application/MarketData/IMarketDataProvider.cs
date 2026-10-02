using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.MarketData;

/// <summary>Supplies canonical market observations through explicitly typed subscriptions.</summary>
/// <remarks>
/// Implementations belong in Infrastructure. Descriptor metadata is stable for the provider instance.
/// Instrument collections must be non-null, non-empty and contain no null IDs. Repeated IDs do not
/// change subscription meaning. Callers must keep collections unchanged throughout consumption.
/// Invalid requests and unsupported capabilities must fail before yielding any observations;
/// validation may occur at the call or when enumeration begins. Cancellation uses standard .NET
/// semantics. No ordering, delivery, deduplication or reconnection guarantees are implied.
/// </remarks>
public interface IMarketDataProvider
{
    MarketDataProviderDescriptor Descriptor { get; }

    /// <summary>Streams quotes for the requested instruments.</summary>
    /// <exception cref="ArgumentNullException">The instrument collection is null.</exception>
    /// <exception cref="ArgumentException">The collection is empty or contains a null ID.</exception>
    /// <exception cref="NotSupportedException">The provider does not advertise Quotes.</exception>
    /// <exception cref="OperationCanceledException">Subscription consumption is canceled.</exception>
    IAsyncEnumerable<Quote> StreamQuotesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken);

    /// <summary>Streams executed trades for the requested instruments.</summary>
    /// <exception cref="ArgumentNullException">The instrument collection is null.</exception>
    /// <exception cref="ArgumentException">The collection is empty or contains a null ID.</exception>
    /// <exception cref="NotSupportedException">The provider does not advertise Trades.</exception>
    /// <exception cref="OperationCanceledException">Subscription consumption is canceled.</exception>
    IAsyncEnumerable<Trade> StreamTradesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken);

    /// <summary>Streams candles at one requested interval for the requested instruments.</summary>
    /// <exception cref="ArgumentNullException">The instrument collection or interval is null.</exception>
    /// <exception cref="ArgumentException">The collection is empty or contains a null ID.</exception>
    /// <exception cref="NotSupportedException">The provider does not advertise Candles.</exception>
    /// <exception cref="OperationCanceledException">Subscription consumption is canceled.</exception>
    IAsyncEnumerable<Candle> StreamCandlesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CandleInterval interval,
        CancellationToken cancellationToken);
}
