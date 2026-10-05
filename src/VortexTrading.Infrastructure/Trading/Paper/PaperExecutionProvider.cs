using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Trading.Paper;

/// <summary>Deterministic paper execution: caller-supplied market observations, finite liquidity.</summary>
/// <remarks>
/// Implements <see cref="IExecutionProvider"/> without a clock, randomness or external system. Every
/// time value is supplied by the caller and every price comes from an observation, so identical
/// inputs always produce identical reports. Matching rules and their documented limits are in
/// ADR-036; the workflow that consumes these reports is the caller's responsibility (ADR-034).
/// <para>
/// The provider never reads <see cref="ReadReportsAsync"/> itself and is not thread-safe: one
/// execution scope owns one provider and one logical report consumer.
/// </para>
/// </remarks>
public sealed class PaperExecutionProvider : IExecutionProvider, IDisposable
{
    private const string DefaultProviderKey = "paper";

    /// <summary>Bounds matching iterations so a pathologically small price cannot spin forever.</summary>
    private const int MaximumFillsPerMatch = 1024;

    private readonly Channel<ExecutionReport> _reports =
        Channel.CreateUnbounded<ExecutionReport>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Dictionary<OrderId, OrderState> _book = [];
    private readonly InstrumentId _instrumentId;
    private PaperMarketObservation? _current;
    private decimal _remainingVolume;
    private UtcInstant? _lastObservationTime;
    private bool _disposed;

    public PaperExecutionProvider(InstrumentId? instrumentId, string providerKey = DefaultProviderKey)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        if (providerKey != providerKey.Trim())
        {
            throw new ArgumentException("Provider key must be trimmed.", nameof(providerKey));
        }

        _instrumentId = instrumentId;
        ProviderKey = providerKey;
    }

    public string ProviderKey { get; }

    /// <summary>Orders still eligible to fill. Exposed for operational inspection during tests.</summary>
    public int RestingOrderCount => _book.Count;

    /// <summary>Publishes one market observation and matches every resting order against it.</summary>
    /// <remarks>
    /// Observations must not move backwards in time; an out-of-order observation is a caller
    /// programming error and fails instead of silently rewriting history. Replaying the same
    /// observation offers its volume again, so the caller decides when volume is fresh.
    /// </remarks>
    public void Observe(PaperMarketObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (observation.InstrumentId != _instrumentId)
        {
            throw new ArgumentException(
                $"Observation targets a different instrument than this provider is configured for.",
                nameof(observation));
        }

        if (_lastObservationTime is not null && observation.EventTime.CompareTo(_lastObservationTime) < 0)
        {
            throw new ArgumentException(
                "Observations must be published in non-decreasing event time.", nameof(observation));
        }

        _lastObservationTime = observation.EventTime;
        _current = observation;
        _remainingVolume = observation.AvailableVolume.Value;
        Match();
    }

    public Task SubmitAsync(OrderSubmission submission, UtcInstant submittedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submittedAt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (submission.InstrumentId != _instrumentId)
        {
            throw new ArgumentException(
                "Submission targets a different instrument than this provider is configured for.",
                nameof(submission));
        }

        if (submittedAt.CompareTo(submission.CreatedAt) < 0)
        {
            throw new ArgumentException("Submission time must not precede creation.", nameof(submittedAt));
        }

        if (_book.ContainsKey(submission.OrderId))
        {
            // ADR-033: equivalent resubmission is the same intent, conflicting content must fail
            // without resending. An in-memory book cannot compare external identity, so any
            // duplicate OrderId is refused rather than silently merged.
            throw new ExecutionProviderException(ExecutionProviderFailureKind.InvalidProviderResponse,
                ProviderKey, submission.OrderId, false, "Order identity is already active in this scope.");
        }

        var state = new OrderState(submission, submittedAt);
        _book.Add(state.OrderId, state);

        // Accepted is available before any fill, so the consumer never sees a fill for an order whose
        // acceptance it has not received through this stream.
        Publish(ExecutionReport.FromAccepted(state.OrderId, submittedAt));
        Match();

        return Task.CompletedTask;
    }

    public Task RequestCancelAsync(OrderId orderId, UtcInstant requestedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(requestedAt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_book.Remove(orderId))
        {
            // Nothing to cancel: unknown, already filled or already cancelled. Manufacturing a
            // Cancelled fact here would assert an external effect that never happened.
            throw new ExecutionProviderException(ExecutionProviderFailureKind.CancellationRefused,
                ProviderKey, orderId, false, "Order is not resting and cannot be cancelled.");
        }

        Publish(ExecutionReport.FromCancelled(orderId, requestedAt));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ExecutionReport> ReadReportsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _reports.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_reports.Reader.TryRead(out var report))
            {
                yield return report;
            }
        }
    }

    /// <summary>Completes the report stream so a consumer can observe shutdown.</summary>
    public void Complete() => _reports.Writer.TryComplete();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
    }

    private void Match()
    {
        var observation = _current;
        if (observation is null || _remainingVolume <= 0m)
        {
            return;
        }

        var fills = 0;
        foreach (var state in _book.Values.ToArray())
        {
            if (_remainingVolume <= 0m || fills >= MaximumFillsPerMatch)
            {
                return;
            }

            if (!_book.ContainsKey(state.OrderId))
            {
                continue;
            }

            if (!TryResolveFillPrice(state, observation, out var fillPrice))
            {
                continue;
            }

            // Fills consume the observation volume in arrival order. The quantity is clamped to the
            // order's exact remainder, so accumulated fills can never exceed the submitted quantity.
            var quantity = Math.Min(_remainingVolume, state.Remaining);
            if (quantity <= 0m)
            {
                continue;
            }

            _remainingVolume -= quantity;
            state.Remaining -= quantity;
            var sequence = state.NextFillSequence++;

            Publish(ExecutionReport.FromFill(new OrderFill(
                PaperExecutionId.From(ProviderKey, state.OrderId, sequence),
                state.OrderId,
                new Quantity(quantity),
                new Price(fillPrice),
                observation.EventTime), observation.EventTime));

            fills++;
            if (state.Remaining <= 0m)
            {
                _book.Remove(state.OrderId);
            }
        }
    }

    /// <summary>Decides whether and at which price an order fills at the observed reference price.</summary>
    /// <remarks>
    /// Market orders fill at the reference price. Limit orders fill only when the reference reaches
    /// the limit, and always at the limit price, never better (ADR-036).
    /// <para>
    /// The observation must be strictly later than the submission. Otherwise an order would fill at a
    /// price that was already known before the intent existed, which is the same look-ahead the replay
    /// semantics forbid. A market order submitted after an observation therefore rests until the next
    /// one arrives.
    /// </para>
    /// </remarks>
    private static bool TryResolveFillPrice(OrderState state, PaperMarketObservation observation,
        out decimal fillPrice)
    {
        fillPrice = 0m;
        if (observation.EventTime.CompareTo(state.SubmittedAt) <= 0)
        {
            return false;
        }

        var referencePrice = observation.ReferencePrice.Value;
        if (state.Type == OrderType.Market)
        {
            fillPrice = referencePrice;
            return true;
        }

        var limit = state.LimitPrice!.Value;
        var reached = state.Side == OrderSide.Buy ? referencePrice <= limit : referencePrice >= limit;
        fillPrice = limit;
        return reached;
    }

    private void Publish(ExecutionReport report)
    {
        if (!_reports.Writer.TryWrite(report))
        {
            // The stream was completed by Complete()/Dispose while an operation was in flight.
            throw new ExecutionProviderException(ExecutionProviderFailureKind.Unavailable,
                ProviderKey, report.OrderId, false, "Report stream is no longer accepting facts.");
        }
    }

    /// <summary>Owns the intent components paper matching needs, without retaining an Order reference.</summary>
    private sealed class OrderState(OrderSubmission submission, UtcInstant submittedAt)
    {
        internal OrderId OrderId { get; } = submission.OrderId;
        internal OrderSide Side { get; } = submission.Side;
        internal OrderType Type { get; } = submission.Type;
        internal Price? LimitPrice { get; } = submission.LimitPrice;

        /// <summary>Only observations strictly later than this instant may fill the order.</summary>
        internal UtcInstant SubmittedAt { get; } = submittedAt;

        /// <summary>Exact remaining quantity; the sum of fills never exceeds the submitted quantity.</summary>
        internal decimal Remaining { get; set; } = submission.Quantity.Value;

        internal long NextFillSequence { get; set; }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"PaperExecutionProvider({ProviderKey}, resting={_book.Count})");
}
