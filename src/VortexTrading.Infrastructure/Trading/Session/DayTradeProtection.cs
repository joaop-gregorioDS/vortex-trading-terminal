using VortexTrading.Application.MarketData;
using VortexTrading.Application.Trading;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>
/// In-memory OCO book for the paper session. Children are real orders. One filled leg cancels the other.
/// A stop moves only after the print that justified it, so that print cannot fill the new price.
/// </summary>
internal sealed class DayTradeProtection
{
    private readonly Dictionary<(InstrumentId Instrument, OrderOrigin Origin), Intent> _intents = [];
    private readonly Dictionary<(InstrumentId Instrument, OrderOrigin Origin), Group> _groups = [];

    public void Clear()
    {
        _intents.Clear();
        _groups.Clear();
    }

    public void Remember(Order order, ProtectionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(order);
        spec.Validate();
        _intents[(order.InstrumentId, order.Origin)] = new Intent(order.Id, spec);
    }

    public string? Role(OrderId orderId)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        foreach (var intent in _intents.Values)
        {
            if (intent.OrderId == orderId)
            {
                return "entry";
            }
        }

        foreach (var group in _groups.Values)
        {
            if (group.TargetId == orderId)
            {
                return "target";
            }

            if (group.StopId == orderId)
            {
                return "stop";
            }
        }

        return null;
    }

    public void Reconcile(PaperTradingSession session, InstrumentId instrument, OrderOrigin origin,
        decimal reference, decimal tickSize, int decimals, UtcInstant? chartBar)
    {
        var quantity = QuantityOf(session, instrument);
        if (quantity == 0m)
        {
            CancelInstrument(session, instrument);
            return;
        }

        // The print that gives birth to the bracket is the entry. Trailing it now would replace the
        // configured loss with the tighter trail before the trade has moved. The next print may.
        if (Arm(session, instrument, origin, quantity, tickSize, decimals, chartBar))
        {
            return;
        }

        if (!_groups.TryGetValue((instrument, origin), out var group))
        {
            return;
        }

        if (Filled(session, group.TargetId) || Filled(session, group.StopId))
        {
            CancelGroup(session, instrument, origin);
            return;
        }

        // A partial exit shrinks the position. The other leg must shrink with it or it would flip the hand.
        if (PartlyFilled(session, group.StopId))
        {
            group.TargetId = Resize(session, group.TargetId, instrument, origin, quantity, chartBar);
        }

        if (PartlyFilled(session, group.TargetId))
        {
            group.StopId = Resize(session, group.StopId, instrument, origin, quantity, chartBar);
        }

        Trail(session, group, quantity, reference, tickSize, decimals);
    }

    private OrderId? Resize(PaperTradingSession session, OrderId? orderId, InstrumentId instrument, OrderOrigin origin,
        decimal signedQuantity, UtcInstant? chartBar)
    {
        if (orderId is null || !session.TryGetOrder(orderId, out var order) || order is null || !IsLive(order)
            || order.LimitPrice is null)
        {
            return orderId;
        }

        var desired = Abs(signedQuantity);
        if (order.RemainingQuantity.Value <= desired)
        {
            return orderId;
        }

        var price = order.LimitPrice.Value;
        var type = order.Type;
        Cancel(session, orderId);
        return SubmitExit(session, instrument, origin, signedQuantity, type, price, desired, chartBar);
    }

    public void NoteRevision(Order order)
    {
        if (!_groups.TryGetValue((order.InstrumentId, order.Origin), out var group))
        {
            return;
        }

        if (group.TargetId != order.Id && group.StopId != order.Id)
        {
            return;
        }

        if (group.TargetId == order.Id)
        {
            group.TargetId = null;
        }

        if (group.StopId == order.Id)
        {
            group.StopId = null;
        }

        if (order.Type == OrderType.Limit)
        {
            group.TargetId = order.Id;
        }
        else if (order.Type == OrderType.Stop)
        {
            group.StopId = order.Id;
        }
    }

    public Order PlaceLevel(PaperTradingSession session, InstrumentId instrument, OrderOrigin origin,
        decimal rawPrice, decimal reference, decimal tickSize, int decimals, UtcInstant? chartBar)
    {
        var quantity = QuantityOf(session, instrument);
        if (quantity == 0m)
        {
            throw new InvalidOperationException("There is no open position to protect.");
        }

        var price = CandleAggregator.RoundToTick(rawPrice, tickSize, decimals);
        if (price <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPrice), "The price must be positive.");
        }

        var side = quantity > 0m ? OrderSide.Sell : OrderSide.Buy;
        var type = ChartOrderMath.Classify(side, price, reference);
        var group = GroupFor(instrument, origin);
        var slot = type == OrderType.Limit ? group.TargetId : group.StopId;
        if (slot is not null && session.TryGetOrder(slot, out var existing) && existing is not null && IsLive(existing))
        {
            session.ReviseResting(existing, price, reference, tickSize, decimals);
            return existing;
        }

        var order = Create(instrument, side, type, Abs(quantity), new Price(price), origin, session.Now());
        Submit(session, order, origin, chartBar);
        if (order.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled)
        {
            if (type == OrderType.Limit)
            {
                group.TargetId = order.Id;
            }
            else
            {
                group.StopId = order.Id;
            }
        }

        return order;
    }

    /// <summary>True when this call placed the target and the stop. The caller then leaves them for a later print.</summary>
    private bool Arm(PaperTradingSession session, InstrumentId instrument, OrderOrigin origin, decimal quantity,
        decimal tickSize, int decimals, UtcInstant? chartBar)
    {
        if (!_intents.TryGetValue((instrument, origin), out var intent))
        {
            return false;
        }

        if (!session.TryGetOrder(intent.OrderId, out var entry) || entry is null)
        {
            return false;
        }

        var filled = entry.FilledQuantity.Value;
        if (filled <= intent.ArmedQuantity)
        {
            return false;
        }

        var sameSide = (entry.Side == OrderSide.Buy && quantity > 0m) || (entry.Side == OrderSide.Sell && quantity < 0m);
        if (!sameSide)
        {
            intent.ArmedQuantity = filled;
            return false;
        }

        var portfolio = session.Portfolio();
        if (!portfolio.Positions.TryGetValue(instrument, out var position) || position.AverageCost <= 0m)
        {
            return false;
        }

        CancelGroup(session, instrument, origin);
        var group = GroupFor(instrument, origin);
        group.BreakEvenDone = false;
        group.TrailDistance = intent.Spec.Trail is { } trail
            ? ProtectionMath.Distance(position.AverageCost, trail, intent.Spec.Unit, tickSize, decimals)
            : null;
        group.BreakEvenDistance = intent.Spec.BreakEven is { } trigger
            ? ProtectionMath.Distance(position.AverageCost, trigger, intent.Spec.Unit, tickSize, decimals)
            : null;

        var (target, stop) = ProtectionMath.Levels(position.AverageCost, quantity, intent.Spec, tickSize, decimals);
        var size = Abs(quantity);
        if (target is { } targetPrice)
        {
            group.TargetId = SubmitExit(session, instrument, origin, quantity, OrderType.Limit, targetPrice, size, chartBar);
        }

        if (stop is { } stopPrice)
        {
            group.StopId = SubmitExit(session, instrument, origin, quantity, OrderType.Stop, stopPrice, size, chartBar);
        }

        intent.ArmedQuantity = filled;
        return true;
    }

    private void Trail(PaperTradingSession session, Group group, decimal quantity, decimal reference,
        decimal tickSize, int decimals)
    {
        if (group.StopId is null || !session.TryGetOrder(group.StopId, out var stop) || stop is null || !IsLive(stop)
            || stop.LimitPrice is null)
        {
            return;
        }

        var current = stop.LimitPrice.Value;
        decimal? next = null;
        if (!group.BreakEvenDone && group.BreakEvenDistance is { } trigger)
        {
            var portfolio = session.Portfolio();
            if (portfolio.Positions.TryGetValue(stop.InstrumentId, out var position))
            {
                next = ProtectionMath.BreakEvenStop(reference, position.AverageCost, current, quantity, trigger, tickSize, decimals);
                if (next is not null)
                {
                    group.BreakEvenDone = true;
                    current = next.Value;
                }
            }
        }

        if (group.TrailDistance is { } trail)
        {
            var trailed = ProtectionMath.TrailedStop(reference, current, quantity, trail, tickSize, decimals);
            if (trailed is not null)
            {
                next = trailed;
            }
        }

        if (next is null || next == stop.LimitPrice.Value)
        {
            return;
        }

        try
        {
            session.ReviseResting(stop, next.Value, reference, tickSize, decimals);
        }
        catch (DomainException)
        {
            // A tighter stop that no longer fits the risk ceiling stays where it was.
        }
    }

    private OrderId? SubmitExit(PaperTradingSession session, InstrumentId instrument, OrderOrigin origin,
        decimal signedQuantity, OrderType type, decimal price, decimal size, UtcInstant? chartBar)
    {
        var side = signedQuantity > 0m ? OrderSide.Sell : OrderSide.Buy;
        var order = Create(instrument, side, type, size, new Price(price), origin, session.Now());
        Submit(session, order, origin, chartBar);
        return order.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled ? order.Id : null;
    }

    private static Order Create(InstrumentId instrument, OrderSide side, OrderType type, decimal quantity, Price price,
        OrderOrigin origin, UtcInstant now) =>
        type == OrderType.Stop
            ? Order.CreateStop(OrderId.From(Guid.NewGuid()), instrument, side, new Quantity(quantity), price,
                TimeInForce.GTC, now, origin)
            : Order.CreateLimit(OrderId.From(Guid.NewGuid()), instrument, side, new Quantity(quantity), price,
                TimeInForce.GTC, now, origin);

    private static void Submit(PaperTradingSession session, Order order, OrderOrigin origin, UtcInstant? chartBar)
    {
        if (origin == OrderOrigin.Chart)
        {
            session.SubmitAsync(order, chartBar, CancellationToken.None).GetAwaiter().GetResult();
            return;
        }

        session.SubmitAsync(order, CancellationToken.None).GetAwaiter().GetResult();
    }

    private void CancelInstrument(PaperTradingSession session, InstrumentId instrument)
    {
        CancelGroup(session, instrument, OrderOrigin.Session);
        CancelGroup(session, instrument, OrderOrigin.Chart);
    }

    private void CancelGroup(PaperTradingSession session, InstrumentId instrument, OrderOrigin origin)
    {
        if (!_groups.TryGetValue((instrument, origin), out var group))
        {
            return;
        }

        Cancel(session, group.TargetId);
        Cancel(session, group.StopId);
        _groups.Remove((instrument, origin));
    }

    private static void Cancel(PaperTradingSession session, OrderId? orderId)
    {
        if (orderId is null || !session.TryGetOrder(orderId, out var order) || order is null || !IsLive(order))
        {
            return;
        }

        try
        {
            session.CancelAsync(orderId, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is InvalidOperationException or ExecutionProviderException)
        {
            // A leg that filled between the check and the cancel has nothing left to withdraw.
        }
    }

    private Group GroupFor(InstrumentId instrument, OrderOrigin origin)
    {
        var key = (instrument, origin);
        if (!_groups.TryGetValue(key, out var group))
        {
            group = new Group();
            _groups[key] = group;
        }

        return group;
    }

    private static decimal QuantityOf(PaperTradingSession session, InstrumentId instrument)
    {
        var portfolio = session.Portfolio();
        return portfolio.Positions.TryGetValue(instrument, out var position) ? position.Quantity : 0m;
    }

    private static bool Filled(PaperTradingSession session, OrderId? orderId) =>
        orderId is not null && session.TryGetOrder(orderId, out var order) && order is not null
        && order.Status == OrderStatus.Filled;

    private static bool PartlyFilled(PaperTradingSession session, OrderId? orderId) =>
        orderId is not null && session.TryGetOrder(orderId, out var order) && order is not null
        && order.Status == OrderStatus.PartiallyFilled;

    private static bool IsLive(Order order) =>
        order.Status is OrderStatus.Accepted or OrderStatus.PartiallyFilled;

    private static decimal Abs(decimal quantity) => quantity < 0m ? ExactDecimal.Negate(quantity) : quantity;

    private sealed class Intent(OrderId orderId, ProtectionSpec spec)
    {
        public OrderId OrderId { get; } = orderId;
        public ProtectionSpec Spec { get; } = spec;
        public decimal ArmedQuantity { get; set; }
    }

    private sealed class Group
    {
        public OrderId? TargetId { get; set; }
        public OrderId? StopId { get; set; }
        public decimal? TrailDistance { get; set; }
        public decimal? BreakEvenDistance { get; set; }
        public bool BreakEvenDone { get; set; }
    }
}
