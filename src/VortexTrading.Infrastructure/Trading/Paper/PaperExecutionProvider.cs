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
/// ADR-036. Session observations and chart observations are separate tapes: each keeps its own clock
/// and its own remaining volume, and each fills only orders of the same origin.
/// <para>
/// This provider does not run a report consumer. It publishes to <see cref="Reports"/>, a buffered
/// channel whose single reader is the execution coordinator, and that consumer takes ownership before
/// any command is issued. The provider is not thread-safe: one execution scope owns one provider.
/// </para>
/// </remarks>
public sealed class PaperExecutionProvider : IExecutionProvider, IPaperMarketObserver, IDisposable
{
    private const string DefaultProviderKey = "paper";

    /// <summary>Bounds matching iterations so a pathologically small price cannot spin forever.</summary>
    private const int MaximumFillsPerMatch = 1024;

    private readonly Channel<ExecutionReport> _reports =
        Channel.CreateUnbounded<ExecutionReport>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Dictionary<OrderId, OrderState> _book = [];
    private readonly Dictionary<OrderOrigin, ObservationTape> _tapes = [];
    private readonly InstrumentId _instrumentId;
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

    /// <summary>Instrument this provider matches. One provider owns exactly one instrument.</summary>
    public InstrumentId InstrumentId => _instrumentId;

    /// <summary>
    /// Buffered stream of normalized facts. Its single logical consumer is the execution coordinator,
    /// which must own it before the first command (ADR-034, ADR-036).
    /// </summary>
    public ChannelReader<ExecutionReport> Reports => _reports.Reader;

    /// <summary>Orders still eligible to fill. Exposed for operational inspection.</summary>
    public int RestingOrderCount => _book.Count;

    /// <summary>
    /// Volume of the latest session observation that no session fill has consumed yet.
    /// Chart observations do not change the simulated book.
    /// </summary>
    public decimal AvailableVolume =>
        _tapes.TryGetValue(OrderOrigin.Session, out var tape) ? tape.RemainingVolume : 0m;

    /// <summary>Drops resting orders and both tapes so a restarted clock can print again.</summary>
    public void DiscardBook()
    {
        _book.Clear();
        _tapes.Clear();
    }

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
                "Observation targets a different instrument than this provider is configured for.",
                nameof(observation));
        }

        var tape = TapeFor(observation.Origin);
        if (tape.LastEventTime is not null && observation.EventTime.CompareTo(tape.LastEventTime) < 0)
        {
            throw new ArgumentException(
                "Observations must be published in non-decreasing event time.", nameof(observation));
        }

        tape.LastEventTime = observation.EventTime;
        tape.Current = observation;
        tape.RemainingVolume = observation.AvailableVolume.Value;
        Match(observation.Origin);
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

        // Accepted is published before any fill, so the consumer never sees a fill for an order whose
        // acceptance it has not received through this stream.
        Publish(ExecutionReport.FromAccepted(state.OrderId, submittedAt));
        Match(state.Origin);

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

    /// <summary>Moves a resting order's price and type. It does not match the observation already on the tape.</summary>
    public bool ReviseResting(OrderId orderId, OrderType type, Price price)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(price);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (type is not (OrderType.Limit or OrderType.Stop) || price.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Only a positive limit or stop can be revised.");
        }

        if (!_book.TryGetValue(orderId, out var state))
        {
            return false;
        }

        state.Type = type;
        state.LimitPrice = price;
        return true;
    }

    /// <summary>
    /// Provided for contract completeness only. This implementation is consumed through
    /// <see cref="Reports"/>; reading it here would create a second consumer and drop facts.
    /// </summary>
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

    /// <summary>Completes the report stream so the consumer can observe shutdown.</summary>
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

    private void Match(OrderOrigin origin)
    {
        if (!_tapes.TryGetValue(origin, out var tape))
        {
            return;
        }

        var observation = tape.Current;
        if (observation is null || tape.RemainingVolume <= 0m)
        {
            return;
        }

        var fills = 0;
        foreach (var state in _book.Values.ToArray())
        {
            if (tape.RemainingVolume <= 0m || fills >= MaximumFillsPerMatch)
            {
                return;
            }

            if (state.Origin != origin)
            {
                continue;
            }

            if (!TryResolveFillPrice(state, observation, out var fillPrice))
            {
                continue;
            }

            // Fills consume the observation volume in arrival order. The quantity is clamped to the
            // order's exact remainder, so accumulated fills can never exceed the submitted quantity.
            var quantity = Math.Min(tape.RemainingVolume, state.Remaining);
            if (quantity <= 0m)
            {
                continue;
            }

            tape.RemainingVolume -= quantity;
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
    /// the limit, and always at the limit price, never better (ADR-036). Stop orders fill when the
    /// reference crosses the trigger, at that reference price: a buy at or above it, a sell at or
    /// below it. A gap through the trigger fills at the observed price, not at the trigger.
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
        var threshold = state.EarliestFillOpen ?? state.SubmittedAt;
        if (observation.EventTime.CompareTo(threshold) <= 0)
        {
            return false;
        }

        var referencePrice = observation.ReferencePrice.Value;
        if (state.Type == OrderType.Market)
        {
            fillPrice = referencePrice;
            return true;
        }

        var price = state.LimitPrice!.Value;
        if (state.Type == OrderType.Stop)
        {
            var crossed = state.Side == OrderSide.Buy
                ? referencePrice >= price
                : referencePrice <= price;
            fillPrice = referencePrice;
            return crossed;
        }

        var reached = state.Side == OrderSide.Buy ? referencePrice <= price : referencePrice >= price;
        fillPrice = price;
        return reached;
    }

    private ObservationTape TapeFor(OrderOrigin origin)
    {
        if (!_tapes.TryGetValue(origin, out var tape))
        {
            tape = new ObservationTape();
            _tapes.Add(origin, tape);
        }

        return tape;
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
        internal OrderType Type { get; set; } = submission.Type;
        internal OrderOrigin Origin { get; } = submission.Origin;
        internal Price? LimitPrice { get; set; } = submission.LimitPrice;

        /// <summary>Only observations strictly later than this instant may fill the order.</summary>
        internal UtcInstant SubmittedAt { get; } = submittedAt;

        /// <summary>Chart orders use the reference bar open instead of the simulated submission instant.</summary>
        internal UtcInstant? EarliestFillOpen { get; } = submission.EarliestFillOpen;

        /// <summary>Exact remaining quantity; the sum of fills never exceeds the submitted quantity.</summary>
        internal decimal Remaining { get; set; } = submission.Quantity.Value;

        internal long NextFillSequence { get; set; }
    }

    /// <summary>One origin's latest price, leftover volume and last event time.</summary>
    private sealed class ObservationTape
    {
        internal PaperMarketObservation? Current { get; set; }
        internal decimal RemainingVolume { get; set; }
        internal UtcInstant? LastEventTime { get; set; }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"PaperExecutionProvider({ProviderKey}, resting={_book.Count})");
}
