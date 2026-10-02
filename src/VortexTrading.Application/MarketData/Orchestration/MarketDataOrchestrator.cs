using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.MarketData.Orchestration;

/// <summary>Validates subscriptions and forwards canonical streams from one provider.</summary>
public sealed class MarketDataOrchestrator
{
    private readonly IMarketDataProvider _provider;

    public MarketDataProviderDescriptor ProviderDescriptor => _provider.Descriptor;

    public MarketDataOrchestrator(IMarketDataProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public IAsyncEnumerable<Quote> StreamQuotesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var subscription = NormalizeSubscription(instrumentIds);
        RequireCapability(MarketDataCapabilities.Quotes);
        return _provider.StreamQuotesAsync(subscription, cancellationToken);
    }

    public IAsyncEnumerable<Trade> StreamTradesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var subscription = NormalizeSubscription(instrumentIds);
        RequireCapability(MarketDataCapabilities.Trades);
        return _provider.StreamTradesAsync(subscription, cancellationToken);
    }

    public IAsyncEnumerable<Candle> StreamCandlesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CandleInterval interval, CancellationToken cancellationToken)
    {
        var subscription = NormalizeSubscription(instrumentIds);
        ArgumentNullException.ThrowIfNull(interval);
        RequireCapability(MarketDataCapabilities.Candles);
        return _provider.StreamCandlesAsync(subscription, interval, cancellationToken);
    }

    private static IReadOnlyCollection<InstrumentId> NormalizeSubscription(IReadOnlyCollection<InstrumentId> instrumentIds)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var snapshot = instrumentIds.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(id => id is null))
        {
            throw new ArgumentException("Subscriptions require at least one instrument and no null IDs.", nameof(instrumentIds));
        }

        return Array.AsReadOnly(snapshot.Distinct().OrderBy(id => id.Value).ToArray());
    }

    private void RequireCapability(MarketDataCapabilities required)
    {
        if ((ProviderDescriptor.Capabilities & required) != required)
        {
            throw new NotSupportedException($"Provider '{ProviderDescriptor.Key}' does not support {required}.");
        }
    }
}
