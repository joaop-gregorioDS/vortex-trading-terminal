using System.Diagnostics.CodeAnalysis;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.ReplayIndicators;

/// <summary>A single-owner serial runtime calculating indicators over accepted causal candle prefixes.</summary>
public sealed class CausalIndicatorReplayRuntime
{
    private readonly CausalReplaySession _session;
    private readonly IndicatorBatchCalculator _calculator = new();
    private readonly Dictionary<(InstrumentId InstrumentId, CandleInterval Interval), List<Candle>> _buffers = [];

    public int Position => _session.Position;
    public UtcInstant CurrentTime => _session.CurrentTime;
    public bool IsCompleted => _session.IsCompleted;
    public bool HasAcceptanceFailure { get; private set; }

    public CausalIndicatorReplayRuntime(HistoricalReplayDataset? dataset)
    {
        _session = new CausalReplaySession(dataset);
    }

    public bool TryMoveNext([NotNullWhen(true)] out ReplayEvent? replayEvent)
    {
        replayEvent = null;
        EnsureHealthy();
        if (!_session.TryMoveNext(out var delivered)) return false;

        if (delivered.Candle is { } candle) Accept(candle);
        replayEvent = delivered;
        return true;
    }

    public MultiSeriesIndicatorResult Calculate(
        InstrumentId instrumentId, CandleInterval interval, IndicatorSpecification specification)
    {
        EnsureHealthy();
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(interval);
        var snapshot = _buffers.TryGetValue((instrumentId, interval), out var buffer)
            ? buffer.ToArray()
            : Array.Empty<Candle>();
        return _calculator.Calculate(specification, snapshot);
    }

    private void Accept(Candle candle)
    {
        var key = (candle.InstrumentId, candle.Interval);
        if (!_buffers.TryGetValue(key, out var buffer))
        {
            _buffers.Add(key, [candle]);
            return;
        }

        var previous = buffer[^1];
        if (candle.OpenTime.CompareTo(previous.OpenTime) <= 0)
        {
            // Replay has already consumed this occurrence; reject before changing the accepted prefix.
            HasAcceptanceFailure = true;
            throw new InvalidOperationException(
                $"Candle acceptance failed at replay position {Position} for instrument {candle.InstrumentId}, " +
                $"interval {candle.Interval}: incoming OpenTime {candle.OpenTime} must be greater than " +
                $"previous accepted OpenTime {previous.OpenTime}.");
        }

        buffer.Add(candle);
    }

    private void EnsureHealthy()
    {
        if (HasAcceptanceFailure)
            throw new InvalidOperationException("Replay indicator runtime has a terminal candle acceptance failure.");
    }
}
