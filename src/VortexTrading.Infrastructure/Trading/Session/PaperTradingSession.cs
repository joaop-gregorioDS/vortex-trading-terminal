using VortexTrading.Application.MarketData;
using VortexTrading.Application.Risk;
using VortexTrading.Application.Trading;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Domain.Markets;
using Microsoft.Extensions.Logging;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.Trading.Paper;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Owns the single paper trading session used by the application.</summary>
/// <remarks>
/// One session, one simulated clock and one lock. Each catalog instrument has its own paper provider
/// and its own execution coordinator, so a print for PETR4 cannot fill an order for VALE3.
/// <para>
/// Time is the simulated instant, not the machine clock. An order can only fill on an observation
/// that is strictly later than its submission.
/// </para>
/// </remarks>
public sealed class PaperTradingSession : IDisposable
{
    /// <summary>Instrument traded by the original single-asset session. It remains the catalog's PETR4.</summary>
    public static InstrumentId InstrumentId => DevelopmentCandleDataset.InstrumentId;

    /// <summary>
    /// Prints retained per instrument. One simulated day at the 15-second step, so a 15-minute chart
    /// and the day high, low and variation still see the session open.
    /// </summary>
    public const int MaximumObservations = 24 * 60 * 60 / 15;

    private readonly SimulationClock _clock;
    private readonly IOrderStore? _store;
    private readonly ILogger<PaperTradingSession>? _logger;
    private readonly IRiskEvaluator? _riskEvaluator;
    private readonly DayTradeProtection _protections = new();
    private readonly Dictionary<InstrumentId, InstrumentScope> _scopes;
    private readonly InstrumentScope _primary;
    private readonly object _gate = new();
    private readonly Lazy<int> _restoredOrders;
    private readonly UtcInstant _marketOpen = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
    private long _feedIndex;
    private UtcInstant _nextPrint;
    private bool _feedRestart;

    public PaperTradingSession(IOrderStore? store = null, ILogger<PaperTradingSession>? logger = null,
        ILogger<ExecutionCoordinator>? coordinatorLogger = null, IRiskEvaluator? riskEvaluator = null)
    {
        _store = store;
        _logger = logger;
        _riskEvaluator = riskEvaluator;
        _clock = new SimulationClock();
        _nextPrint = _marketOpen;
        _scopes = new Dictionary<InstrumentId, InstrumentScope>(CandleAggregator.Catalog.Count);

        InstrumentScope? primary = null;
        foreach (var profile in CandleAggregator.Catalog)
        {
            var provider = new PaperExecutionProvider(profile.InstrumentId);
            var coordinator = new ExecutionCoordinator(profile.InstrumentId, provider, _clock, provider.Reports,
                store, coordinatorLogger);
            var scope = new InstrumentScope(profile, provider, coordinator);
            _scopes.Add(profile.InstrumentId, scope);
            if (profile.InstrumentId == InstrumentId)
            {
                primary = scope;
            }
        }

        _primary = primary ?? throw new InvalidOperationException(
            "The simulated catalog does not contain the session's primary instrument.");
        Coordinator = _primary.Coordinator;

        // Restoration is deferred and fault tolerant on purpose. A database outage must not stop the
        // process from booting; readiness reports that failure separately.
        _restoredOrders = new Lazy<int>(RestoreOrders, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Execution scope of the primary instrument. Other instruments have their own scopes.</summary>
    public ExecutionCoordinator Coordinator { get; }

    /// <summary>Current simulated instant, or null before the feed produced anything.</summary>
    public UtcInstant? CurrentTime => _clock.Current;

    /// <summary>Instant to stamp new intents with. Does not consult the machine clock.</summary>
    public UtcInstant Now() => _clock.Now();

    /// <summary>Most recent reference price of the primary instrument.</summary>
    public Price? LastPrice
    {
        get
        {
            lock (_gate)
            {
                return _primary.LastPrice;
            }
        }
    }

    /// <summary>Orders resting across every instrument book.</summary>
    public int RestingOrderCount
    {
        get
        {
            lock (_gate)
            {
                return _scopes.Values.Sum(scope => scope.Provider.RestingOrderCount);
            }
        }
    }

    /// <summary>True when this session has a book for the instrument.</summary>
    public bool Trades(InstrumentId? instrumentId) =>
        instrumentId is not null && _scopes.ContainsKey(instrumentId);

    /// <summary>
    /// Rebuilds the portfolio from the fills the session owns, in execution order.
    /// </summary>
    public Portfolio Portfolio()
    {
        EnsureRecovered();

        lock (_gate)
        {
            return BuildPortfolio();
        }
    }

    /// <summary>
    /// One round of the simulated feed. A demonstration reset is applied here, under the same lock,
    /// so the next print never moves backwards.
    /// </summary>
    public void PublishFeedRound(TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The simulated step must be positive.");
        }

        lock (_gate)
        {
            if (_feedRestart)
            {
                _feedIndex = 0;
                _nextPrint = _marketOpen;
                _clock.Restart(_marketOpen);
                _feedRestart = false;
            }

            var instant = _nextPrint;
            var index = _feedIndex;
            foreach (var profile in CandleAggregator.Catalog)
            {
                Publish(profile.InstrumentId, instant, SimulatedPricePath.PriceAt(profile, index), profile.PrintQuantity);
            }

            _feedIndex = index + 1;
            _nextPrint = new UtcInstant(instant.Value.Add(step));
        }
    }

    /// <summary>Erases the shared demonstration account and asks the feed to open a new session.</summary>
    public void ResetDemonstration()
    {
        lock (_gate)
        {
            _store?.DeleteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            foreach (var scope in _scopes.Values)
            {
                scope.Provider.DiscardBook();
                scope.Coordinator.DiscardTrackedOrders();
                scope.Observations.Clear();
                scope.LastPrice = null;
            }

            _protections.Clear();
            _feedRestart = true;
            _feedIndex = 0;
            _nextPrint = _marketOpen;
            _clock.Restart(_marketOpen);
        }
    }

    /// <summary>Publishes one print for the primary instrument.</summary>
    public void Publish(UtcInstant eventTime, decimal referencePrice, decimal volume) =>
        Publish(InstrumentId, eventTime, referencePrice, volume);

    /// <summary>Publishes one print for a catalog instrument and matches only that book.</summary>
    public void Publish(InstrumentId instrumentId, UtcInstant eventTime, decimal referencePrice, decimal volume)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(eventTime);

        lock (_gate)
        {
            if (!_scopes.TryGetValue(instrumentId, out var scope))
            {
                throw new ArgumentException("The instrument is not traded by this session.", nameof(instrumentId));
            }

            _clock.AdvanceTo(eventTime);
            var price = new Price(referencePrice);
            scope.LastPrice = price;
            scope.Observations.Add(new PriceObservation(eventTime, referencePrice, volume));
            if (scope.Observations.Count > MaximumObservations)
            {
                scope.Observations.RemoveRange(0, scope.Observations.Count - MaximumObservations);
            }

            scope.Coordinator.Observe(new PaperMarketObservation(eventTime, instrumentId, price, new Quantity(volume)));
            Reconcile(scope, OrderOrigin.Session, referencePrice, chartBar: null);
        }
    }

    /// <summary>
    /// Publishes one chart close for matching chart orders. It does not move the simulated clock,
    /// the quote or the book.
    /// </summary>
    public void ObserveChart(InstrumentId instrumentId, UtcInstant barOpen, decimal close, decimal volume)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(barOpen);
        if (close <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(close), "Chart close must be positive.");
        }

        if (volume <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Chart volume must be positive.");
        }

        lock (_gate)
        {
            if (!_scopes.TryGetValue(instrumentId, out var scope))
            {
                throw new ArgumentException("The instrument is not traded by this session.", nameof(instrumentId));
            }

            scope.Coordinator.Observe(
                new PaperMarketObservation(barOpen, instrumentId, new Price(close), new Quantity(volume),
                    OrderOrigin.Chart),
                _clock.Now());
            Reconcile(scope, OrderOrigin.Chart, close, barOpen);
        }
    }

    /// <summary>Recent prints of the primary instrument, oldest first.</summary>
    public IReadOnlyList<PriceObservation> Observations => ObservationsFor(InstrumentId);

    /// <summary>Recent prints of one instrument, oldest first. Empty when the instrument is unknown.</summary>
    public IReadOnlyList<PriceObservation> ObservationsFor(InstrumentId instrumentId)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);

        lock (_gate)
        {
            return _scopes.TryGetValue(instrumentId, out var scope)
                ? scope.Observations.ToArray()
                : [];
        }
    }

    /// <summary>Last price, leftover tick volume, prints and resting orders of one book.</summary>
    public InstrumentBookView ReadBook(InstrumentId instrumentId)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        EnsureRecovered();

        lock (_gate)
        {
            if (!_scopes.TryGetValue(instrumentId, out var scope))
            {
                throw new ArgumentException("The instrument is not traded by this session.", nameof(instrumentId));
            }

            var resting = scope.Coordinator.Orders
                .Where(order => order.Origin == OrderOrigin.Session)
                .Where(order => order.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                .ToArray();
            return new InstrumentBookView(scope.LastPrice, scope.Provider.AvailableVolume,
                scope.Observations.ToArray(), resting);
        }
    }

    /// <summary>Last price of one instrument, or null when it has not printed or is unknown.</summary>
    public Price? LastPriceFor(InstrumentId instrumentId)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);

        lock (_gate)
        {
            return _scopes.TryGetValue(instrumentId, out var scope) ? scope.LastPrice : null;
        }
    }

    /// <summary>Fills across every book, for reports that must not keep a second copy of the ledger.</summary>
    public IReadOnlyList<TradeFill> TradeFills()
    {
        EnsureRecovered();
        lock (_gate)
        {
            return AllOrders()
                .SelectMany(order => order.Fills.Select(fill =>
                {
                    var known = CandleAggregator.TryGetProfile(order.InstrumentId, out var profile) && profile is not null;
                    return new TradeFill(
                        order.InstrumentId,
                        known ? profile!.Ticker : order.InstrumentId.ToString(),
                        order.Side,
                        fill.Quantity.Value,
                        fill.Price.Value,
                        known ? profile!.PointValue : 1m,
                        fill.ExecutedAt,
                        fill.ExecutionId.ToString());
                }))
                .ToArray();
        }
    }

    /// <summary>Money still open, using each instrument's point value. A position without a mark contributes nothing.</summary>
    public decimal UnrealizedProfitAndLoss()
    {
        var portfolio = Portfolio();
        var unrealized = 0m;
        foreach (var position in portfolio.Positions.Values)
        {
            var mark = LastPriceFor(position.InstrumentId);
            if (mark is null || position.IsClosed)
            {
                continue;
            }

            var point = CandleAggregator.TryGetProfile(position.InstrumentId, out var profile) && profile is not null
                ? profile.PointValue
                : 1m;
            unrealized = Domain.Common.ExactDecimal.Add(unrealized,
                Domain.Common.ExactDecimal.Multiply(position.UnrealizedProfitAndLoss(mark), point, 8));
        }

        return unrealized;
    }

    /// <summary>Orders owned by every book, after recovery.</summary>
    public IReadOnlyList<Order> Orders
    {
        get
        {
            EnsureRecovered();
            lock (_gate)
            {
                return AllOrders();
            }
        }
    }

    /// <summary>Finds an order in whichever book owns it.</summary>
    public bool TryGetOrder(OrderId orderId, out Order? order)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        EnsureRecovered();

        lock (_gate)
        {
            foreach (var scope in _scopes.Values)
            {
                if (scope.Coordinator.TryGetOrder(orderId, out order))
                {
                    return true;
                }
            }

            order = null;
            return false;
        }
    }

    /// <summary>Submits an order to the book of its instrument.</summary>
    public Task SubmitAsync(Order order, CancellationToken cancellationToken)
        => SubmitAsync(order, referenceBarOpen: null, cancellationToken);

    /// <summary>
    /// Submits a chart order. <paramref name="referenceBarOpen"/> is the candle already open when the
    /// order was sent; that candle cannot fill it.
    /// </summary>
    public Task SubmitAsync(Order order, UtcInstant? referenceBarOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        EnsureRecovered();

        lock (_gate)
        {
            if (!_scopes.TryGetValue(order.InstrumentId, out var scope))
            {
                throw new InvalidOperationException("Order targets an instrument this session does not trade.");
            }

            if (order.Origin == OrderOrigin.Chart && referenceBarOpen is null)
            {
                throw new InvalidOperationException("A chart order needs the open time of its reference candle.");
            }

            if (order.Origin != OrderOrigin.Chart && referenceBarOpen is not null)
            {
                throw new InvalidOperationException("Only a chart order carries a reference candle.");
            }

            if (_riskEvaluator is not null)
            {
                var activeOrders = AllOrders()
                    .Where(candidate => candidate.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                    .ToArray();
                var context = new RiskEvaluationContext(order, BuildPortfolio(), scope.LastPrice, activeOrders,
                    scope.Profile.PointValue);
                var decision = _riskEvaluator.Evaluate(context);
                if (!decision.IsApproved)
                {
                    var at = _clock.Now();
                    order.Reject(OrderRejectionReason.PreTradeValidationFailed, at);
                    _logger?.LogWarning(
                        "Order {OrderId} rejected by pre-trade risk check: {Reason}",
                        order.Id.Value, decision.Reason);
                    return scope.Coordinator.RecordPreTradeRejectedOrderAsync(order, at, cancellationToken);
                }
            }

            return scope.Coordinator.SubmitOrderAsync(order, _clock.Now(), cancellationToken, referenceBarOpen);
        }
    }

    /// <summary>Records a bracket that arms when <paramref name="order"/> gains a fill on its own tape.</summary>
    public void RememberProtection(Order order, ProtectionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(order);
        EnsureRecovered();
        lock (_gate)
        {
            if (order.Status is OrderStatus.Rejected or OrderStatus.Cancelled)
            {
                return;
            }

            _protections.Remember(order, spec);
        }
    }

    /// <summary>"entry", "target", "stop", or null when the order is not part of a bracket.</summary>
    public string? ProtectionRole(OrderId orderId)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        EnsureRecovered();
        lock (_gate)
        {
            return _protections.Role(orderId);
        }
    }

    /// <summary>Market order that flattens one position and leaves resting orders in the book. Null when flat.</summary>
    public Order? Flatten(InstrumentId instrumentId) => SubmitClosing(instrumentId, reverse: false);

    /// <summary>Market order of twice the open quantity, opposite the position. Null when flat.</summary>
    public Order? Reverse(InstrumentId instrumentId) => SubmitClosing(instrumentId, reverse: true);

    /// <summary>Flattens every open position. Resting orders are left alone.</summary>
    public int FlattenAll()
    {
        EnsureRecovered();
        lock (_gate)
        {
            var count = 0;
            foreach (var position in BuildPortfolio().Positions.Values.Where(item => item.Quantity != 0m).ToArray())
            {
                var order = SubmitClosingLocked(position.InstrumentId, reverse: false);
                if (order is not null && order.Status != OrderStatus.Rejected)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Moves a resting order. The reference price chooses limit or stop again. The current tape is not rematched.
    /// </summary>
    public void ReviseResting(Order order, decimal rawPrice, decimal reference, decimal tickSize, int decimals)
    {
        ArgumentNullException.ThrowIfNull(order);
        EnsureRecovered();
        lock (_gate)
        {
            if (!_scopes.TryGetValue(order.InstrumentId, out var scope))
            {
                throw new InvalidOperationException("Order targets an instrument this session does not trade.");
            }

            if (order.Status is not (OrderStatus.Accepted or OrderStatus.PartiallyFilled))
            {
                throw new InvalidOperationException("Order is not resting.");
            }

            var price = CandleAggregator.RoundToTick(rawPrice, tickSize, decimals);
            if (price <= 0m || reference <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(rawPrice), "The revised price must be positive.");
            }

            var type = ChartOrderMath.Classify(order.Side, price, reference);
            if (_riskEvaluator is not null)
            {
                var probe = type == OrderType.Stop
                    ? Order.CreateStop(order.Id, order.InstrumentId, order.Side, order.Quantity, new Price(price),
                        order.TimeInForce, order.CreatedAt, order.Origin)
                    : Order.CreateLimit(order.Id, order.InstrumentId, order.Side, order.Quantity, new Price(price),
                        order.TimeInForce, order.CreatedAt, order.Origin);
                var active = AllOrders()
                    .Where(candidate => candidate.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                    .ToArray();
                var decision = _riskEvaluator.Evaluate(new RiskEvaluationContext(
                    probe, BuildPortfolio(), new Price(reference), active, scope.Profile.PointValue));
                if (!decision.IsApproved)
                {
                    throw new DomainException("The revised order exceeds a risk limit.");
                }
            }

            if (!scope.Provider.ReviseResting(order.Id, type, new Price(price)))
            {
                throw new InvalidOperationException("Order is not resting.");
            }

            order.Revise(type, new Price(price), _clock.Now());
            _protections.NoteRevision(order);
            if (_store is not null)
            {
                _store.SaveAsync([order], CancellationToken.None).GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>Adds or moves the exit leg a drag selected. The reference price chooses target or stop.</summary>
    public Order PlaceProtection(InstrumentId instrumentId, OrderOrigin origin, decimal rawPrice, decimal reference,
        decimal tickSize, int decimals, UtcInstant? chartBar)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        EnsureRecovered();
        lock (_gate)
        {
            if (!_scopes.ContainsKey(instrumentId))
            {
                throw new InvalidOperationException("Order targets an instrument this session does not trade.");
            }

            if (origin == OrderOrigin.Chart && chartBar is null)
            {
                throw new InvalidOperationException("A chart order needs the open time of its reference candle.");
            }

            return _protections.PlaceLevel(this, instrumentId, origin, rawPrice, reference, tickSize, decimals, chartBar);
        }
    }

    private Order? SubmitClosing(InstrumentId instrumentId, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        EnsureRecovered();
        lock (_gate)
        {
            return SubmitClosingLocked(instrumentId, reverse);
        }
    }

    private Order? SubmitClosingLocked(InstrumentId instrumentId, bool reverse)
    {
        if (!_scopes.ContainsKey(instrumentId))
        {
            throw new InvalidOperationException("Order targets an instrument this session does not trade.");
        }

        var signed = BuildPortfolio().Positions.TryGetValue(instrumentId, out var position) ? position.Quantity : 0m;
        if (signed == 0m)
        {
            return null;
        }

        var abs = signed < 0m ? ExactDecimal.Negate(signed) : signed;
        var size = reverse ? ExactDecimal.Add(abs, abs) : abs;
        var side = signed > 0m ? OrderSide.Sell : OrderSide.Buy;
        var order = Order.CreateMarket(OrderId.From(Guid.NewGuid()), instrumentId, side, new Quantity(size),
            TimeInForce.GTC, _clock.Now());
        SubmitAsync(order, CancellationToken.None).GetAwaiter().GetResult();
        return order;
    }

    private void Reconcile(InstrumentScope scope, OrderOrigin origin, decimal reference, UtcInstant? chartBar)
    {
        var (tick, decimals) = TickFor(scope.Profile, origin);
        _protections.Reconcile(this, scope.Profile.InstrumentId, origin, reference, tick, decimals, chartBar);
    }

    private static (decimal Tick, int Decimals) TickFor(AssetMarketProfile profile, OrderOrigin origin)
    {
        if (origin == OrderOrigin.Chart && YahooSeriesCatalog.TryForProfile(profile, out var series))
        {
            return (series.TickSize, series.Decimals);
        }

        return (profile.TickSize, profile.Decimals);
    }

    /// <summary>Requests cancellation at the current simulated instant, on the book that owns the order.</summary>
    public Task CancelAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        EnsureRecovered();

        lock (_gate)
        {
            foreach (var scope in _scopes.Values)
            {
                if (scope.Coordinator.TryGetOrder(orderId, out _))
                {
                    return scope.Coordinator.RequestCancellationAsync(orderId, _clock.Now(), cancellationToken);
                }
            }

            throw new InvalidOperationException("Order is not owned by this execution scope.");
        }
    }

    public void Dispose()
    {
        foreach (var scope in _scopes.Values)
        {
            scope.Provider.Dispose();
        }
    }

    /// <summary>How many persisted orders were restored into a catalog book.</summary>
    public int RestoredOrderCount
    {
        get
        {
            EnsureRecovered();
            return _restoredOrders.Value;
        }
    }

    /// <summary>Performs recovery if it has not happened yet.</summary>
    public void EnsureRecovered() => _ = _restoredOrders.Value;

    private Portfolio BuildPortfolio()
    {
        var portfolio = Domain.Portfolio.Portfolio.Empty();
        var fills = AllOrders()
            .SelectMany(order => order.Fills.Select(fill => (order, fill)))
            .OrderBy(entry => entry.fill.ExecutedAt)
            .ThenBy(entry => entry.order.Id.Value)
            .ToArray();

        foreach (var (order, fill) in fills)
        {
            portfolio = portfolio.Apply(order.InstrumentId, fill, order.Side, allowShort: true).Portfolio;
        }

        return portfolio;
    }

    private Order[] AllOrders() =>
        _scopes.Values.SelectMany(scope => scope.Coordinator.Orders).ToArray();

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
                var restored = 0;
                foreach (var order in orders)
                {
                    if (!_scopes.TryGetValue(order.InstrumentId, out var scope))
                    {
                        _logger?.LogWarning(
                            "Skipping persisted order {OrderId} for instrument {InstrumentId}, which this session does not trade.",
                            order.Id.Value, order.InstrumentId.Value);
                        continue;
                    }

                    scope.Coordinator.Restore(order);
                    restored++;
                }

                if (restored > 0)
                {
                    _logger?.LogInformation("Restored {OrderCount} persisted orders into the execution scope.", restored);
                }

                return restored;
            }
            catch (Exception error)
            {
                _logger?.LogWarning(error, "Persisted orders could not be restored; continuing with an empty book.");
                return 0;
            }
        }
    }

    private sealed class InstrumentScope(
        AssetMarketProfile profile,
        PaperExecutionProvider provider,
        ExecutionCoordinator coordinator)
    {
        public AssetMarketProfile Profile { get; } = profile;

        public PaperExecutionProvider Provider { get; } = provider;

        public ExecutionCoordinator Coordinator { get; } = coordinator;

        public List<PriceObservation> Observations { get; } = [];

        public Price? LastPrice { get; set; }
    }

    /// <summary>Clock driven by the feed. It never reads the machine clock.</summary>
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

        internal void Restart(UtcInstant instant)
        {
            _current = instant;
            Current = null;
        }

        public UtcInstant Now() => _current;
    }
}
