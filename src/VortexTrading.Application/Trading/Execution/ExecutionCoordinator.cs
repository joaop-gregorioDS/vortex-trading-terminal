using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Owns one execution scope: one report consumer, one workflow per order, explicit time.</summary>
/// <remarks>
/// ADR-034 deliberately leaves stream ownership, routing, time selection and shutdown to the caller,
/// and warns that a future coordinator "may own one scope consumer and dispatch
/// OrderId -> OrderExecutionWorkflow". This is that owner, and nothing more: it adds no persistence,
/// no background pump, no retry and no reconciliation.
/// <para>
/// Reports are applied by draining the buffered stream at well-defined points, so an operation's
/// effects are visible when it returns. That makes coordination deterministic and testable, and it
/// requires the channel to be buffered. A provider whose reports arrive asynchronously would need a
/// running consumer instead, which is a separate decision and is why this class takes the reader as
/// an explicit constructor input rather than reaching into the provider.
/// </para>
/// <para>
/// Not thread-safe. All operations must be serial, and accesses to each order must be serial too.
/// </para>
/// </remarks>
public sealed class ExecutionCoordinator
{
    private readonly Dictionary<OrderId, OrderExecutionWorkflow> _workflows = [];
    private readonly Dictionary<OrderId, Order> _orders = [];
    private readonly HashSet<OrderId> _submitted = [];
    private readonly InstrumentId _instrumentId;
    private readonly IExecutionProvider _provider;
    private readonly IApplicationClock _clock;
    private readonly ChannelReader<ExecutionReport> _reports;
    private readonly IOrderStore? _store;
    private readonly ILogger<ExecutionCoordinator>? _logger;

    public ExecutionCoordinator(InstrumentId? instrumentId, IExecutionProvider? provider,
        IApplicationClock? clock, ChannelReader<ExecutionReport>? reports, IOrderStore? store = null,
        ILogger<ExecutionCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(reports);

        _instrumentId = instrumentId;
        _provider = provider;
        _clock = clock;
        _reports = reports;
        _store = store;
        _logger = logger;
    }

    /// <summary>Instrument owned by this scope. One scope owns exactly one instrument.</summary>
    public InstrumentId InstrumentId => _instrumentId;

    /// <summary>Orders currently owned by this scope.</summary>
    public int TrackedOrderCount => _workflows.Count;

    /// <summary>True when a submission attempt was already recorded for this order.</summary>
    public bool WasSubmitted(OrderId? orderId)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        return _submitted.Contains(orderId);
    }

    /// <summary>Resolves the workflow that owns an order, when this scope owns it.</summary>
    /// <remarks>
    /// Read-only lookup for callers that need the order's current state. It never creates a workflow,
    /// so it cannot be used to acquire ownership by accident.
    /// </remarks>
    public bool TryGet(OrderId? orderId, out OrderExecutionWorkflow? workflow)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        var found = _workflows.TryGetValue(orderId, out var resolved);
        workflow = resolved;
        return found;
    }

    /// <summary>Resolves an order owned by this scope, when present.</summary>
    /// <remarks>
    /// The scope keeps the orders it owns because the workflow deliberately exposes no accessor for
    /// them (ADR-034). Returning the live instance is safe only under the serial ownership rule this
    /// class already requires; callers must not mutate it.
    /// </remarks>
    public bool TryGetOrder(OrderId? orderId, out Order? order)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        var found = _orders.TryGetValue(orderId, out var resolved);
        order = resolved;
        return found;
    }

    /// <summary>Orders owned by this scope, in the order they were submitted.</summary>
    public IReadOnlyList<Order> Orders => _orders.Values.ToArray();

    /// <summary>Submits one created order and applies any fact the provider publishes immediately.</summary>
    public async Task SubmitOrderAsync(Order? order, UtcInstant? submittedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.InstrumentId != _instrumentId)
        {
            throw new InvalidOperationException("Order targets a different instrument than this scope.");
        }

        if (_workflows.ContainsKey(order.Id))
        {
            throw new InvalidOperationException("Order is already owned by this execution scope.");
        }

        if (_submitted.Contains(order.Id))
        {
            // The scope owns the fact that an attempt was already made, which the workflow cannot
            // report without widening its frozen public shape (ADR-034).
            throw new InvalidOperationException("Order was already submitted in this execution scope.");
        }

        var at = submittedAt ?? _clock.Now();
        var workflow = new OrderExecutionWorkflow(order, _provider);

        // Register before submitting: the provider may publish a fact during the command, and routing
        // must already know the owner.
        _workflows.Add(order.Id, workflow);
        _orders.Add(order.Id, order);
        _submitted.Add(order.Id);

        try
        {
            await workflow.SubmitOrderAsync(at, cancellationToken).ConfigureAwait(false);
            DrainReports(at);
            await PersistAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Order submitted {OrderId} {Side} {Type} {Quantity} {LimitPrice} at {SubmittedAt}",
                order.Id.Value, order.Side, order.Type, order.Quantity.Value,
                order.LimitPrice?.Value, at.Value);
        }
        catch (Exception error)
        {
            // Never leave a half-owned order behind; the caller decides what to do with the order.
            _workflows.Remove(order.Id);
            _orders.Remove(order.Id);

            _logger?.LogWarning(error, "Order submission failed {OrderId} at {SubmittedAt}",
                order.Id.Value, at.Value);
            throw;
        }
    }

    /// <summary>Requests cancellation through the order's workflow and applies the outcome.</summary>
    public async Task RequestCancellationAsync(OrderId? orderId, UtcInstant? requestedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        var at = requestedAt ?? _clock.Now();
        var workflow = Owned(orderId);

        DrainReports(at);
        await workflow.RequestCancellationAsync(at, cancellationToken).ConfigureAwait(false);
        DrainReports(at);
        await PersistAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Cancellation requested {OrderId} at {RequestedAt}",
            orderId.Value, at.Value);
    }

    /// <summary>Publishes a market observation and applies every fact it produces.</summary>
    /// <remarks>
    /// The application time for the produced facts is the observation's event time, because that is
    /// the instant the facts belong to. The caller supplies it; no clock is consulted here.
    /// </remarks>
    public void Observe(PaperMarketObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.InstrumentId != _instrumentId)
        {
            throw new InvalidOperationException("Observation targets a different instrument than this scope.");
        }

        DrainReports(observation.EventTime);
        if (_provider is IPaperMarketObserver observer)
        {
            observer.Observe(observation);
        }
        else
        {
            throw new InvalidOperationException(
                "The execution provider of this scope cannot consume market observations.");
        }

        var applied = DrainReports(observation.EventTime);

        // Persist only when the observation actually changed something. Observations arrive on a timer
        // and most ticks fill nothing, so an unconditional save would rewrite the whole book every tick.
        if (applied > 0)
        {
            PersistAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
    /// <summary>Registers an order that was loaded from durable storage, without resubmitting it.</summary>
    /// <remarks>
    /// Used on startup so the scope owns what it previously persisted and its positions can be derived.
    /// <para>
    /// Only an order still in <see cref="OrderStatus.Created"/> gets a workflow, because the workflow
    /// constructor requires exactly that state and fabricating command history for a partially filled
    /// order would assert attempts that never happened. A restored live order therefore appears in the
    /// book and in position calculations but cannot be commanded until a decision is made about how to
    /// recover an in-flight execution. That gap is deliberate and must not be papered over.
    /// </para>
    /// </remarks>
    public void Restore(Order? order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.InstrumentId != _instrumentId)
        {
            throw new InvalidOperationException("Order targets a different instrument than this scope.");
        }

        if (!_orders.TryAdd(order.Id, order))
        {
            return;
        }

        if (order.Status == OrderStatus.Created)
        {
            _ = _workflows.TryAdd(order.Id, new OrderExecutionWorkflow(order, _provider));
        }

        _submitted.Add(order.Id);
    }

    /// <summary>Applies every buffered fact at the supplied application time.</summary>
    /// <remarks>
    /// The application time must not precede the last update of any affected order, which is the
    /// domain rule the workflow preserves. Callers that mix several instants in one batch can trip
    /// that guard; pass the instant the batch belongs to.
    /// </remarks>
    public int DrainReports(UtcInstant at)
    {
        ArgumentNullException.ThrowIfNull(at);
        var applied = 0;

        while (_reports.TryRead(out var report))
        {
            if (!_workflows.TryGetValue(report.OrderId, out var workflow))
            {
                // The provider published a fact for an order this scope does not own, which is a
                // contract violation. Silently dropping it would hide a real bug.
                throw new InvalidOperationException(
                    "Execution report targets an order that is not owned by this execution scope.");
            }

            var changed = workflow.ApplyReport(report, at);
            applied++;

            // One structured event per applied fact, emitted here because every fact passes through
            // this loop. Values are named fields rather than interpolated text so a log query can group
            // by order, kind or price without parsing prose.
            if (changed && _logger is not null)
            {
                var fill = report.Fill;
                _logger.LogInformation(
                    "Execution fact applied {Kind} {OrderId} {Quantity} {Price} at {AppliedAt}",
                    report.Kind, report.OrderId.Value,
                    fill?.Quantity.Value, fill?.Price.Value, at.Value);
            }
        }

        return applied;
    }

    /// <summary>Resolves the workflow that owns an order, or fails.</summary>
    public OrderExecutionWorkflow Owned(OrderId orderId)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        return _workflows.TryGetValue(orderId, out var workflow)
            ? workflow
            : throw new InvalidOperationException("Order is not owned by this execution scope.");
    }

    /// <summary>Writes the owned orders to durable storage, when one is configured.</summary>
    /// <remarks>
    /// Every order is saved, not only the one that just changed, because the store contract is a
    /// wholesale save and the aggregate carries its fills as part of itself.
    /// </remarks>
    private Task PersistAsync(CancellationToken cancellationToken) =>
        _store is null || _orders.Count == 0
            ? Task.CompletedTask
            : _store.SaveAsync(_orders.Values.ToArray(), cancellationToken);
}
