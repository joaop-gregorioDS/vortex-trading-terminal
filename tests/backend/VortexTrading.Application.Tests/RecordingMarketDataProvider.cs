using System.Runtime.CompilerServices;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Tests;

internal sealed class RecordingMarketDataProvider : IMarketDataProvider
{
    public MarketDataProviderDescriptor Descriptor { get; }
    public IReadOnlyList<Quote> Quotes { get; init; } = [];
    public IReadOnlyList<Trade> Trades { get; init; } = [];
    public IReadOnlyList<Candle> Candles { get; init; } = [];
    public Exception? CallException { get; init; }
    public Exception? EnumerationException { get; init; }
    public int QuoteCalls { get; private set; }
    public int TradeCalls { get; private set; }
    public int CandleCalls { get; private set; }
    public int TotalCalls => QuoteCalls + TradeCalls + CandleCalls;
    public int EnumerationStarts { get; private set; }
    public int EmittedEvents { get; private set; }
    public int DisposedEnumerations { get; private set; }
    public IReadOnlyCollection<InstrumentId>? Subscription { get; private set; }
    public CancellationToken OperationToken { get; private set; }
    public CandleInterval? RequestedInterval { get; private set; }
    public object? ReturnedStream { get; private set; }

    public RecordingMarketDataProvider(MarketDataCapabilities capabilities)
    {
        Descriptor = new MarketDataProviderDescriptor("recording", "Recording Provider", capabilities);
    }

    public IAsyncEnumerable<Quote> StreamQuotesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        QuoteCalls++;
        RecordRequest(instrumentIds, cancellationToken);
        var stream = StreamAsync(Quotes, cancellationToken);
        ReturnedStream = stream;
        return stream;
    }

    public IAsyncEnumerable<Trade> StreamTradesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        TradeCalls++;
        RecordRequest(instrumentIds, cancellationToken);
        var stream = StreamAsync(Trades, cancellationToken);
        ReturnedStream = stream;
        return stream;
    }

    public IAsyncEnumerable<Candle> StreamCandlesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds, CandleInterval interval, CancellationToken cancellationToken)
    {
        CandleCalls++;
        RequestedInterval = interval;
        RecordRequest(instrumentIds, cancellationToken);
        var stream = StreamAsync(Candles, cancellationToken);
        ReturnedStream = stream;
        return stream;
    }

    private void RecordRequest(IReadOnlyCollection<InstrumentId> instrumentIds, CancellationToken cancellationToken)
    {
        Subscription = instrumentIds;
        OperationToken = cancellationToken;
        if (CallException is not null)
        {
            throw CallException;
        }
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(
        IReadOnlyList<T> events, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationStarts++;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            foreach (var observation in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EmittedEvents++;
                yield return observation;
                if (EnumerationException is not null)
                {
                    throw EnumerationException;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            DisposedEnumerations++;
        }
    }
}
