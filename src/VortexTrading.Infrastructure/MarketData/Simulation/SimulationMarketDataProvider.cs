using System.Runtime.CompilerServices;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Infrastructure.MarketData.Simulation;

public sealed class SimulationMarketDataProvider : IMarketDataProvider
{
    private readonly SimulationMarketDataOptions _options;

    public MarketDataProviderDescriptor Descriptor { get; } = new("simulation", "Simulation",
        MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles);

    public SimulationMarketDataProvider(SimulationMarketDataOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public IAsyncEnumerable<Quote> StreamQuotesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var ids = NormalizeSubscription(instrumentIds);
        return StreamAsync(ids, TimeSpan.TicksPerSecond,
            (id, time) => new Quote(id, time, time, _options.Price, _options.Quantity, _options.Price, _options.Quantity),
            cancellationToken);
    }

    public IAsyncEnumerable<Trade> StreamTradesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        var ids = NormalizeSubscription(instrumentIds);
        return StreamAsync(ids, TimeSpan.TicksPerSecond,
            (id, time) => new Trade(id, time, time, _options.Price, _options.Quantity), cancellationToken);
    }

    public IAsyncEnumerable<Candle> StreamCandlesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CandleInterval interval, CancellationToken cancellationToken)
    {
        var ids = NormalizeSubscription(instrumentIds);
        ArgumentNullException.ThrowIfNull(interval);

        var remainingTicks = DateTimeOffset.MaxValue.UtcTicks - _options.StartTime.Value.UtcTicks;
        if (interval.Duration.Ticks > remainingTicks / _options.EventCount)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The complete simulation candle series must have representable close times.");
        }

        return StreamAsync(ids, interval.Duration.Ticks,
            (id, time) => new Candle(id, interval, time, _options.Price, _options.Price,
                _options.Price, _options.Price, _options.Quantity), cancellationToken);
    }

    private static InstrumentId[] NormalizeSubscription(IReadOnlyCollection<InstrumentId> instrumentIds)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var snapshot = instrumentIds.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(id => id is null))
        {
            throw new ArgumentException("Simulation subscriptions require at least one instrument and no null IDs.", nameof(instrumentIds));
        }

        return snapshot.Distinct().OrderBy(id => id.Value).ToArray();
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(
        InstrumentId[] ids, long intervalTicks, Func<InstrumentId, UtcInstant, T> createObservation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // In-memory data is immediately available; no artificial delay or background producer.
        await Task.CompletedTask;

        for (var index = 0; index < _options.EventCount; index++)
        {
            var time = new UtcInstant(_options.StartTime.Value.AddTicks(checked((long)index * intervalTicks)));
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return createObservation(id, time);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
