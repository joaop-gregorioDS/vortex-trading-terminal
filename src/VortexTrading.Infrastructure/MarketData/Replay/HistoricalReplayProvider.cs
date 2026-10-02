using System.Runtime.CompilerServices;
using VortexTrading.Application.MarketData;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Infrastructure.MarketData.Replay;

public sealed class HistoricalReplayProvider : IMarketDataProvider
{
    private readonly HistoricalReplayDataset _dataset;

    public MarketDataProviderDescriptor Descriptor { get; } = new("historical-replay", "Historical Replay",
        MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles);

    public HistoricalReplayProvider(HistoricalReplayDataset? dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    public IAsyncEnumerable<Quote> StreamQuotesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var subscription = SnapshotSubscription(instrumentIds);
        return ProjectAsync(subscription,
            replayEvent => replayEvent.Kind == ReplayEventKind.Quote ? replayEvent.Quote : null, cancellationToken);
    }

    public IAsyncEnumerable<Trade> StreamTradesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var subscription = SnapshotSubscription(instrumentIds);
        return ProjectAsync(subscription,
            replayEvent => replayEvent.Kind == ReplayEventKind.Trade ? replayEvent.Trade : null, cancellationToken);
    }

    public IAsyncEnumerable<Candle> StreamCandlesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CandleInterval interval, CancellationToken cancellationToken)
    {
        var subscription = SnapshotSubscription(instrumentIds);
        ArgumentNullException.ThrowIfNull(interval);
        return ProjectAsync(subscription,
            replayEvent => replayEvent.Kind == ReplayEventKind.Candle && replayEvent.Candle!.Interval == interval
                ? replayEvent.Candle : null, cancellationToken);
    }

    private static HashSet<InstrumentId> SnapshotSubscription(IReadOnlyCollection<InstrumentId> instrumentIds)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var snapshot = instrumentIds.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(id => id is null))
        {
            throw new ArgumentException("Replay subscriptions require at least one instrument and no null IDs.", nameof(instrumentIds));
        }

        return snapshot.ToHashSet();
    }

    private async IAsyncEnumerable<T> ProjectAsync<T>(
        HashSet<InstrumentId> subscription, Func<ReplayEvent, T?> selectPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The immutable in-memory dataset needs no pacing or background producer.
        await Task.CompletedTask;

        foreach (var replayEvent in _dataset.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subscription.Contains(replayEvent.InstrumentId))
            {
                var payload = selectPayload(replayEvent);
                if (payload is not null)
                {
                    yield return payload;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
