using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using Microsoft.Extensions.Logging;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.Trading.Paper;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Owns the single paper trading session used by the application.</summary>
/// <remarks>
/// A web application has no ambient notion of "the current order book", so exactly one component must
/// own the execution scope, the provider and the market feed. That is this singleton.
/// <para>
/// The session deliberately owns no HTTP concern and no persistence. It exposes the coordinator, the
/// virtual clock and the market snapshot so that endpoints stay thin and the simulation stays testable
/// without a web host.
/// </para>
/// <para>
/// Time is <b>the simulated instant</b>, not the machine clock. Every order and every report is
/// timestamped from the feed, which is what keeps execution reproducible and keeps the causal rule
/// intact: an order can only fill on an observation that is strictly later than its submission.
/// </para>
/// </remarks>
public sealed class PaperTradingSession : IDisposable
{
    /// <summary>Instrument traded by the simulated session.</summary>
    public static InstrumentId InstrumentId => DevelopmentCandleDataset.InstrumentId;

    /// <summary>How many observations are retained for charting.</summary>
    public const int MaximumObservations = 300;

    private readonly PaperExecutionProvider _provider;
    private readonly SimulationClock _clock;
    private readonly IOrderStore? _store;
    private readonly ILogger<PaperTradingSession>? _logger;
    private readonly List<PriceObservation> _observations = [];
    private readonly object _gate = new();
    private readonly Lazy<int> _restoredOrders;

    public PaperTradingSession(IOrderStore? store = null, ILogger<PaperTradingSession>? logger = null)
    {
        _store = store;
        _logger = logger;
        _clock = new SimulationClock();
        _provider = new PaperExecutionProvider(InstrumentId);
        Coordinator = new ExecutionCoordinator(InstrumentId, _provider, _clock, _provider.Reports, store);

        // Restoration is deferred and fault tolerant on purpose. The persistence foundation states that
        // a valid configuration starts even when the database is unavailable and that readiness reports
        // the failure; throwing here would turn a database outage into a process that cannot boot.
        _restoredOrders = new Lazy<int>(RestoreOrders, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Execution scope that owns every order of this session.</summary>
    public ExecutionCoordinator Coordinator { get; }

    /// <summary>Current simulated instant, or null before the feed produced anything.</summary>
    public UtcInstant? CurrentTime => _clock.Current;

    /// <summary>
    /// Instant to stamp new intents with. Falls back to the clock's start when the feed has not
    /// produced anything yet, so an order can be created before the first observation without the
    /// endpoint consulting the machine clock.
    /// </summary>
    public UtcInstant Now() => _clock.Now();

    /// <summary>Most recent reference price, or null before the feed produced anything.</summary>
    public Price? LastPrice { get; private set; }

    /// <summary>Orders resting at the provider.</summary>
    public int RestingOrderCount => _provider.RestingOrderCount;

    /// <summary>
    /// Rebuilds the portfolio from the fills the session owns, in execution order.
    /// </summary>
    /// <remarks>
    /// The portfolio is derived, never stored. Positions would otherwise be a second copy of state
    /// that the fills already determine, and the two could disagree after any partial failure. The
    /// cost is that this walks every fill on each read, which is the right trade while the session is
    /// in memory and the fill count is small; a persisted projection would be the answer at a scale
    /// this application does not have yet.
    /// </remarks>
    public Portfolio Portfolio()
    {
        // Recovery is triggered here as well as from the endpoints, so a caller that reaches the
        // session directly still sees the restored history rather than an empty book.
        EnsureRecovered();

        lock (_gate)
        {
            // Fully qualified because this member shares its name with the type it returns.
            var portfolio = Domain.Portfolio.Portfolio.Empty();
            var fills = Coordinator.Orders
                .SelectMany(order => order.Fills.Select(fill => (order, fill)))
                .OrderBy(entry => entry.fill.ExecutedAt)
                .ToArray();

            foreach (var (order, fill) in fills)
            {
                portfolio = portfolio.Apply(InstrumentId, fill, order.Side).Portfolio;
            }

            return portfolio;
        }
    }

    /// <summary>Publishes one simulated observation and applies everything it produces.</summary>
    /// <remarks>
    /// Serialized: the coordinator is not thread-safe and the feed may be driven by a timer while an
    /// operator submits an order. The lock is the session's only concurrency mechanism, and it is the
    /// reason endpoints can stay simple.
    /// </remarks>
    public void Publish(UtcInstant eventTime, decimal referencePrice, decimal volume)
    {
        ArgumentNullException.ThrowIfNull(eventTime);

        lock (_gate)
        {
            _clock.AdvanceTo(eventTime);
            LastPrice = new Price(referencePrice);
            _observations.Add(new PriceObservation(eventTime, referencePrice, volume));

            // Bounded on purpose: the feed is finite today, but an unbounded list in a singleton would
            // grow for the life of the process if a longer feed were ever introduced.
            if (_observations.Count > MaximumObservations)
            {
                _observations.RemoveRange(0, _observations.Count - MaximumObservations);
            }

            Coordinator.Observe(new PaperMarketObservation(eventTime, InstrumentId,
                LastPrice, new Quantity(volume)));
        }
    }

    /// <summary>Recent observations this session published, oldest first.</summary>
    /// <remarks>
    /// A bounded history of what the session actually traded against. The chart needs a price series,
    /// and the demonstration fixture does not exist outside Development, so without this the hosted
    /// terminal would have a market panel and no chart at all.
    /// </remarks>
    public IReadOnlyList<PriceObservation> Observations
    {
        get
        {
            lock (_gate)
            {
                return _observations.ToArray();
            }
        }
    }

    /// <summary>Submits an order at the current simulated instant.</summary>
    /// <remarks>
    /// The caller keeps the <see cref="Order"/> it passed in and observes its lifecycle through that
    /// instance, which the workflow mutates in place.
    /// </remarks>
    public Task SubmitAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Recovery runs before the first mutation so a new order is never written alongside a history
        // that has not been loaded yet, which would rewrite existing rows from a partial view.
        EnsureRecovered();

        lock (_gate)
        {
            return Coordinator.SubmitOrderAsync(order, _clock.Now(), cancellationToken);
        }
    }

    /// <summary>Requests cancellation at the current simulated instant.</summary>
    public Task CancelAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        EnsureRecovered();

        lock (_gate)
        {
            return Coordinator.RequestCancellationAsync(orderId, _clock.Now(), cancellationToken);
        }
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>
    /// Loads persisted orders into the scope once, on first use, and reports how many were restored.
    /// </summary>
    /// <remarks>
    /// The scope cannot be handed its history at construction without making a database outage fatal,
    /// so recovery happens on the first operation that needs it. A failed load is logged and treated as
    /// "nothing restored" rather than propagated: trading continues in memory and readiness keeps
    /// reporting the real problem.
    /// </remarks>
    public int RestoredOrderCount
    {
        get
        {
            EnsureRecovered();
            return _restoredOrders.Value;
        }
    }

    /// <summary>Performs recovery if it has not happened yet.</summary>
    /// <remarks>
    /// Every read of owned state must call this first. Recovery being implicit and triggered only by
    /// some operations made the order list and the order-by-id endpoint report an empty book after a
    /// restart while the portfolio showed restored positions, which is exactly the kind of
    /// inconsistency a lazy initialization must not create.
    /// </remarks>
    public void EnsureRecovered() => _ = _restoredOrders.Value;

    private int RestoreOrders()
    {
        if (_store is null)
        {
            return 0;
        }

        lock (_gate)
        {
            try
            {
                var orders = _store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                foreach (var order in orders)
                {
                    Coordinator.Restore(order);
                }

                if (orders.Count > 0)
                {
                    _logger?.LogInformation("Restored {OrderCount} persisted orders into the execution scope.",
                        orders.Count);
                }

                return orders.Count;
            }
            catch (Exception error)
            {
                _logger?.LogWarning(error,
                    "Persisted orders could not be restored; continuing with an empty book.");
                return 0;
            }
        }
    }

    /// <summary>
    /// Clock driven by the feed instead of the machine. It exists so the coordinator can default an
    /// instant without any component consulting the wall clock (ADR-033, ADR-037).
    /// </summary>
    private sealed class SimulationClock : IApplicationClock
    {
        private UtcInstant _current = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

        internal UtcInstant? Current { get; private set; }

        internal void AdvanceTo(UtcInstant instant)
        {
            if (Current is null || instant.CompareTo(Current) >= 0)
            {
                _current = instant;
                Current = instant;
            }
        }

        public UtcInstant Now() => _current;
    }
}
