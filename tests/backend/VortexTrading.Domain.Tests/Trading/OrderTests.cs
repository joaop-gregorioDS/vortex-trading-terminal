using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests.Trading;

public sealed class OrderTests
{
    private static readonly OrderId Id = new(new Guid("e68ea44b-b074-4d16-92ce-fc9a4dc518a0"));
    private static readonly InstrumentId InstrumentId = new(new Guid("9d0f6823-2e3b-40ef-a5ba-5d9e9b7179dc"));
    private static readonly UtcInstant Created = new(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    [Theory]
    [InlineData(OrderSide.Buy, false)]
    [InlineData(OrderSide.Sell, false)]
    [InlineData(OrderSide.Buy, true)]
    [InlineData(OrderSide.Sell, true)]
    public void FactoriesProduceValidInitialIntents(OrderSide side, bool limit)
    {
        var quantity = new Quantity(10.2500m);
        var price = new Price(10.0500m);
        var order = limit
            ? Order.CreateLimit(Id, InstrumentId, side, quantity, price, TimeInForce.GTC, Created)
            : Order.CreateMarket(Id, InstrumentId, side, quantity, TimeInForce.GTC, Created);

        Assert.Same(Id, order.Id);
        Assert.Same(InstrumentId, order.InstrumentId);
        Assert.Equal(side, order.Side);
        Assert.Equal(limit ? OrderType.Limit : OrderType.Market, order.Type);
        Assert.Same(quantity, order.Quantity);
        Assert.Equal(decimal.GetBits(10.2500m), decimal.GetBits(order.Quantity.Value));
        Assert.Equal(TimeInForce.GTC, order.TimeInForce);
        Assert.Equal(OrderOrigin.Session, order.Origin);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Same(Created, order.CreatedAt);
        Assert.Same(Created, order.LastUpdatedAt);
        Assert.Equal(0m, order.FilledQuantity.Value);
        Assert.Equal(10.25m, order.RemainingQuantity.Value);
        Assert.Empty(order.Fills);
        Assert.Null(order.RejectionReason);
        Assert.Null(order.CompletedAt);
        if (limit)
        {
            Assert.NotNull(order.LimitPrice);
            Assert.Same(price, order.LimitPrice);
            Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(order.LimitPrice.Value));
        }
        else
        {
            Assert.Null(order.LimitPrice);
        }
    }

    [Theory]
    [InlineData("Id", false)]
    [InlineData("InstrumentId", false)]
    [InlineData("Quantity", false)]
    [InlineData("CreatedAt", false)]
    [InlineData("Id", true)]
    [InlineData("InstrumentId", true)]
    [InlineData("Quantity", true)]
    [InlineData("CreatedAt", true)]
    public void FactoriesRejectMissingCommonComponents(string missing, bool limit)
    {
        var id = missing == "Id" ? null : Id;
        var instrument = missing == "InstrumentId" ? null : InstrumentId;
        var quantity = missing == "Quantity" ? null : new Quantity(1m);
        var at = missing == "CreatedAt" ? null : Created;
        Assert.Throws<DomainException>(() => limit
            ? Order.CreateLimit(id, instrument, OrderSide.Buy, quantity, new Price(10m), TimeInForce.GTC, at)
            : Order.CreateMarket(id, instrument, OrderSide.Buy, quantity, TimeInForce.GTC, at));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoriesRejectZeroRequestedQuantity(bool limit)
    {
        Assert.Throws<DomainException>(() => limit
            ? Order.CreateLimit(Id, InstrumentId, OrderSide.Buy, new Quantity(0m), new Price(10m), TimeInForce.GTC, Created)
            : Order.CreateMarket(Id, InstrumentId, OrderSide.Buy, new Quantity(0m), TimeInForce.GTC, Created));
    }

    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(99, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 99, false)]
    [InlineData(-1, 0, true)]
    [InlineData(99, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(0, 99, true)]
    public void FactoriesRejectUnsupportedEnumCasts(int side, int timeInForce, bool limit)
    {
        Assert.Throws<DomainException>(() => limit
            ? Order.CreateLimit(Id, InstrumentId, (OrderSide)side, new Quantity(1m), new Price(10m), (TimeInForce)timeInForce, Created)
            : Order.CreateMarket(Id, InstrumentId, (OrderSide)side, new Quantity(1m), (TimeInForce)timeInForce, Created));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LimitRequiresAPresentPositivePrice(bool absent)
    {
        Assert.Throws<DomainException>(() => Order.CreateLimit(Id, InstrumentId, OrderSide.Buy,
            new Quantity(1m), absent ? null : new Price(0m), TimeInForce.GTC, Created));
    }

    [Fact]
    public void StopDefaultsToTheChartTapeAndKeepsTheTriggerPrice()
    {
        var trigger = new Price(209335m);
        var order = Order.CreateStop(Id, InstrumentId, OrderSide.Buy, new Quantity(1m), trigger,
            TimeInForce.GTC, Created);

        Assert.Equal(OrderType.Stop, order.Type);
        Assert.Equal(OrderOrigin.Chart, order.Origin);
        Assert.Same(trigger, order.LimitPrice);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void LimitCanRestOnTheChartTape()
    {
        var order = Order.CreateLimit(Id, InstrumentId, OrderSide.Sell, new Quantity(1m), new Price(56m),
            TimeInForce.GTC, Created, OrderOrigin.Chart);

        Assert.Equal(OrderType.Limit, order.Type);
        Assert.Equal(OrderOrigin.Chart, order.Origin);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopRequiresAPresentPositiveTrigger(bool absent)
    {
        Assert.Throws<DomainException>(() => Order.CreateStop(Id, InstrumentId, OrderSide.Buy,
            new Quantity(1m), absent ? null : new Price(0m), TimeInForce.GTC, Created));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void StopRejectsAnUndefinedOrigin(int origin)
    {
        Assert.Throws<DomainException>(() => Order.CreateStop(Id, InstrumentId, OrderSide.Buy,
            new Quantity(1m), new Price(10m), TimeInForce.GTC, Created, (OrderOrigin)origin));
    }

    [Theory]
    [InlineData(OrderSide.Buy, 109, false)]
    [InlineData(OrderSide.Buy, 110, true)]
    [InlineData(OrderSide.Buy, 130, true)]
    [InlineData(OrderSide.Sell, 91, true)]
    [InlineData(OrderSide.Sell, 100, true)]
    [InlineData(OrderSide.Sell, 101, false)]
    public void StopFillsMustCrossTheTrigger(OrderSide side, int price, bool allowed)
    {
        var trigger = side == OrderSide.Buy ? 110m : 100m;
        var order = Order.CreateStop(Id, InstrumentId, side, new Quantity(1m), new Price(trigger),
            TimeInForce.GTC, Created);
        order.Accept(At(1));
        if (allowed)
        {
            Assert.True(order.ApplyFill(Fill(1, 1m, price), At(2)));
            Assert.Equal(OrderStatus.Filled, order.Status);
        }
        else
        {
            UnchangedFailure(order, () => order.ApplyFill(Fill(1, 1m, price), At(2)));
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.12500")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    public void PositiveRequestedQuantityIsPreservedWithoutContextualRestrictions(string text)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        var order = Market(value);
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(order.Quantity.Value));
        Assert.Equal(value, order.RemainingQuantity.Value);
    }

    [Fact]
    public void AcceptanceUpdatesOnlyLifecycleAndUsesExplicitTickPrecision()
    {
        var order = Market();
        var at = At(1);
        order.Accept(at);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Same(at, order.LastUpdatedAt);
        Assert.Same(Created, order.CreatedAt);
        Assert.Null(order.CompletedAt);
        Assert.Null(order.RejectionReason);
        Assert.Empty(order.Fills);
    }

    [Theory]
    [InlineData(OrderStatus.Created, OrderRejectionReason.PreTradeValidationFailed)]
    [InlineData(OrderStatus.Created, OrderRejectionReason.ExecutionProviderRejected)]
    [InlineData(OrderStatus.Accepted, OrderRejectionReason.ExecutionProviderRejected)]
    public void AllowedRejectionsStoreCanonicalReasonAndCompletion(OrderStatus state, OrderRejectionReason reason)
    {
        var order = InState(state);
        var at = At(10);
        order.Reject(reason, at);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(reason, order.RejectionReason);
        Assert.Same(at, order.LastUpdatedAt);
        Assert.Same(at, order.CompletedAt);
        Assert.Empty(order.Fills);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidRejectionReasonCannotMutateAnOrder(int reason)
    {
        var order = Market();
        UnchangedFailure(order, () => order.Reject((OrderRejectionReason)reason, At(10)));
    }

    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Accepted)]
    [InlineData(OrderStatus.PartiallyFilled)]
    public void CancellationPreservesPreviouslyExecutedAndUnexecutedQuantities(OrderStatus state)
    {
        var order = InState(state);
        var fills = order.Fills.ToArray();
        var filled = order.FilledQuantity;
        var remaining = order.RemainingQuantity;
        var at = At(10);
        order.Cancel(at);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Same(at, order.CompletedAt);
        Assert.Equal(fills, order.Fills);
        Assert.Equal(filled, order.FilledQuantity);
        Assert.Equal(remaining, order.RemainingQuantity);
        Assert.Null(order.RejectionReason);
    }

    public static TheoryData<OrderStatus, string> IllegalTransitions
    {
        get
        {
            var cases = new TheoryData<OrderStatus, string>();
            foreach (var state in Enum.GetValues<OrderStatus>())
            {
                foreach (var operation in new[] { "Accept", "PreTradeReject", "ProviderReject", "Cancel", "Fill" })
                {
                    var allowed = operation switch
                    {
                        "Accept" or "PreTradeReject" => state == OrderStatus.Created,
                        "ProviderReject" => state is OrderStatus.Created or OrderStatus.Accepted,
                        "Cancel" => state is OrderStatus.Created or OrderStatus.Accepted or OrderStatus.PartiallyFilled,
                        "Fill" => state is OrderStatus.Accepted or OrderStatus.PartiallyFilled,
                        _ => false
                    };
                    if (!allowed)
                    {
                        cases.Add(state, operation);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void AllUnsupportedStateTransitionsAreAtomicFailures(OrderStatus state, string operation)
    {
        var order = InState(state);
        UnchangedFailure(order, () => Operate(order, operation, At(10)));
    }

    [Theory]
    [InlineData("Accept", false)]
    [InlineData("PreTradeReject", false)]
    [InlineData("ProviderReject", false)]
    [InlineData("Cancel", false)]
    [InlineData("Fill", false)]
    [InlineData("Accept", true)]
    [InlineData("PreTradeReject", true)]
    [InlineData("ProviderReject", true)]
    [InlineData("Cancel", true)]
    [InlineData("Fill", true)]
    public void MissingOrRegressiveApplicationTimeCannotMutateState(string operation, bool missing)
    {
        var order = operation == "Fill" ? InState(OrderStatus.Accepted) : Market();
        UnchangedFailure(order, () => Operate(order, operation, missing ? null : At(-1)));
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("PreTradeReject")]
    [InlineData("ProviderReject")]
    [InlineData("Cancel")]
    [InlineData("Fill")]
    public void EqualApplicationInstantsArePermitted(string operation)
    {
        var order = Market();
        if (operation == "Fill")
        {
            order.Accept(Created);
        }

        Operate(order, operation, Created);
        Assert.Same(Created, order.LastUpdatedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Accepted, "ProviderReject", 0)]
    [InlineData(OrderStatus.Accepted, "Cancel", 0)]
    [InlineData(OrderStatus.PartiallyFilled, "Cancel", 1)]
    [InlineData(OrderStatus.PartiallyFilled, "Fill", 1)]
    public void ApplicationTimeMustNotRegressEvenWhenItIsAfterCreation(OrderStatus state, string operation, int ticks)
    {
        var order = InState(state);
        UnchangedFailure(order, () => Operate(order, operation, At(ticks)));
    }

    [Fact]
    public void SuccessiveMarketFillsPreserveOriginalInstancesAndReachCompletion()
    {
        var order = Market(10m);
        order.Accept(At(1));
        var first = Fill(1, 3m, 400m);
        var second = Fill(2, 2m, 0.01m);
        var last = Fill(3, 5m, decimal.MaxValue);
        Assert.True(order.ApplyFill(first, At(2)));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(3m, order.FilledQuantity.Value);
        Assert.Equal(7m, order.RemainingQuantity.Value);
        Assert.Null(order.CompletedAt);
        Assert.True(order.ApplyFill(second, At(3)));
        Assert.Equal(5m, order.FilledQuantity.Value);
        Assert.Equal(5m, order.RemainingQuantity.Value);
        Assert.True(order.ApplyFill(last, At(4)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(10m, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(At(4), order.CompletedAt);
        Assert.Equal(new[] { first, second, last }, order.Fills);
        Assert.Same(first, order.Fills[0]);
    }

    [Fact]
    public void DirectFullFillTerminatesTheOrder()
    {
        var order = InState(OrderStatus.Accepted);
        Assert.True(order.ApplyFill(Fill(1, 1m), At(2)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(1m, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(At(2), order.CompletedAt);
    }

    [Theory]
    [InlineData(OrderSide.Buy, 9, true)]
    [InlineData(OrderSide.Buy, 10, true)]
    [InlineData(OrderSide.Buy, 11, false)]
    [InlineData(OrderSide.Sell, 11, true)]
    [InlineData(OrderSide.Sell, 10, true)]
    [InlineData(OrderSide.Sell, 9, false)]
    public void LimitFillsRespectTheSideSpecificPriceBound(OrderSide side, int price, bool allowed)
    {
        var order = Order.CreateLimit(Id, InstrumentId, side, new Quantity(1m), new Price(10m), TimeInForce.GTC, Created);
        order.Accept(At(1));
        if (allowed)
        {
            Assert.True(order.ApplyFill(Fill(1, 1m, price), At(2)));
            Assert.Equal(OrderStatus.Filled, order.Status);
        }
        else
        {
            UnchangedFailure(order, () => order.ApplyFill(Fill(1, 1m, price), At(2)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndPartialOverfillsAreAtomicFailures(bool partial)
    {
        var order = InState(partial ? OrderStatus.PartiallyFilled : OrderStatus.Accepted);
        UnchangedFailure(order, () => order.ApplyFill(Fill(2, 1.01m), At(10)));
    }

    [Fact]
    public void NullFillAndWrongOrderAreAtomicFailures()
    {
        var order = InState(OrderStatus.Accepted);
        UnchangedFailure(order, () => order.ApplyFill(null, At(2)));
        var other = new OrderId(new Guid("00473f79-beb4-4eee-9cba-ad4b9e06f1d1"));
        UnchangedFailure(order, () => order.ApplyFill(new OrderFill(Execution(1), other,
            new Quantity(0.25m), new Price(10m), Created), At(2)));
    }

    [Theory]
    [InlineData(OrderStatus.PartiallyFilled)]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    public void EquivalentRedeliveryIsANoopEvenAfterCompletionAndWithRegressiveAt(OrderStatus state)
    {
        var order = InState(state);
        var original = order.Fills[0];
        var same = new OrderFill(new ExecutionId(original.ExecutionId.Value), new OrderId(Id.Value),
            new Quantity(original.Quantity.Value * 1.00m), new Price(10.000m),
            new UtcInstant(original.ExecutedAt.Value.ToOffset(TimeSpan.FromHours(-3))));
        var last = order.LastUpdatedAt;
        var completed = order.CompletedAt;
        var count = order.Fills.Count;
        Assert.False(order.ApplyFill(same, At(-100)));
        Assert.Equal(state, order.Status);
        Assert.Same(last, order.LastUpdatedAt);
        Assert.Same(completed, order.CompletedAt);
        Assert.Equal(count, order.Fills.Count);
        Assert.Same(original, order.Fills[0]);
    }

    [Theory]
    [InlineData("Quantity", OrderStatus.PartiallyFilled)]
    [InlineData("Price", OrderStatus.PartiallyFilled)]
    [InlineData("ExecutedAt", OrderStatus.PartiallyFilled)]
    [InlineData("OrderId", OrderStatus.PartiallyFilled)]
    [InlineData("Quantity", OrderStatus.Filled)]
    [InlineData("Price", OrderStatus.Filled)]
    [InlineData("ExecutedAt", OrderStatus.Filled)]
    [InlineData("OrderId", OrderStatus.Filled)]
    [InlineData("Quantity", OrderStatus.Cancelled)]
    [InlineData("Price", OrderStatus.Cancelled)]
    [InlineData("ExecutedAt", OrderStatus.Cancelled)]
    [InlineData("OrderId", OrderStatus.Cancelled)]
    public void ConflictingRedeliveryNeverMutatesState(string changed, OrderStatus state)
    {
        var order = InState(state);
        var previous = order.Fills[0];
        var conflict = new OrderFill(previous.ExecutionId,
            changed == "OrderId" ? new OrderId(InstrumentId.Value) : Id,
            new Quantity(changed == "Quantity" ? previous.Quantity.Value + 0.1m : previous.Quantity.Value),
            new Price(changed == "Price" ? 11m : 10m),
            changed == "ExecutedAt" ? At(-10) : previous.ExecutedAt);
        UnchangedFailure(order, () => order.ApplyFill(conflict, At(20)));
    }

    [Fact]
    public void DifferentExecutionIdsWithIdenticalEconomicsAreTwoFills()
    {
        var order = Market();
        order.Accept(At(1));
        Assert.True(order.ApplyFill(Fill(1, 0.25m), At(2)));
        Assert.True(order.ApplyFill(Fill(2, 0.25m), At(2)));
        Assert.Equal(2, order.Fills.Count);
        Assert.Equal(0.5m, order.FilledQuantity.Value);
        Assert.Equal(0.5m, order.RemainingQuantity.Value);
    }

    [Fact]
    public void EquivalentRedeliveryStillRequiresAnExplicitNonNullApplicationArgument()
    {
        var order = InState(OrderStatus.Filled);
        UnchangedFailure(order, () => order.ApplyFill(order.Fills[0], null));
    }

    [Fact]
    public void ExecutedAtCanPrecedeCreationAndBeOutOfOrderWithoutRewriting()
    {
        var order = Market();
        order.Accept(At(1));
        var first = new OrderFill(Execution(1), Id, new Quantity(0.25m), new Price(10m), At(-2));
        var earlier = new OrderFill(Execution(2), Id, new Quantity(0.25m), new Price(10m), At(-20));
        order.ApplyFill(first, At(2));
        order.ApplyFill(earlier, At(3));
        Assert.Same(first.ExecutedAt, order.Fills[0].ExecutedAt);
        Assert.Same(earlier.ExecutedAt, order.Fills[1].ExecutedAt);
        Assert.Equal(At(3), order.LastUpdatedAt);
    }

    [Fact]
    public void OrderEqualityAndHashAreStableAcrossDifferentIntentAndLifecycle()
    {
        var first = Market();
        var second = Order.CreateLimit(new OrderId(Id.Value), InstrumentId, OrderSide.Sell,
            new Quantity(20m), new Price(999m), TimeInForce.GTC, At(100));
        var hash = first.GetHashCode();
        var set = new HashSet<Order> { first };
        first.Accept(At(1));
        first.ApplyFill(Fill(1, 1m), At(2));
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(hash, first.GetHashCode());
        Assert.Equal(hash, second.GetHashCode());
        Assert.Contains(second, set);
        Assert.NotEqual(first, Order.CreateMarket(new OrderId(InstrumentId.Value), InstrumentId,
            OrderSide.Buy, new Quantity(1m), TimeInForce.GTC, Created));
        Assert.False(first.Equals((Order?)null));
        Assert.False(first.Equals("other"));
        Assert.True((Order?)null == null);
        Assert.True(first != null);
    }

    [Fact]
    public void ExternalConsumersCannotMutateCollectionOrIntentAndDerivedProperties()
    {
        var order = InState(OrderStatus.PartiallyFilled);
        var view = order.Fills;
        Assert.False(view is List<OrderFill>);
        var list = Assert.IsAssignableFrom<IList<OrderFill>>(view);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(Fill(2, 0.25m)));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = Fill(2, 0.25m));
        foreach (var property in typeof(Order).GetProperties())
        {
            Assert.False(property.SetMethod?.IsPublic == true, property.Name);
        }

        foreach (var property in typeof(OrderFill).GetProperties())
        {
            Assert.Null(property.SetMethod);
        }

        order.ApplyFill(Fill(2, 0.25m), At(3));
        Assert.Same(view, order.Fills);
        Assert.Equal(2, view.Count);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void IdenticalExplicitInputsProduceIdenticalObservableResultsAcrossCultures(string culture)
    {
        var first = Market();
        first.Accept(At(1));
        first.ApplyFill(Fill(1, 0.25m), At(2));
        first.Cancel(At(3));
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var second = Market();
            second.Accept(At(1));
            second.ApplyFill(Fill(1, 0.25m), At(2));
            second.Cancel(At(3));
            Assert.Equal(first.Id, second.Id);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.Fills, second.Fills);
            Assert.Equal(first.FilledQuantity, second.FilledQuantity);
            Assert.Equal(first.RemainingQuantity, second.RemainingQuantity);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Equal(first.LastUpdatedAt, second.LastUpdatedAt);
            Assert.Equal(first.CompletedAt, second.CompletedAt);
            Assert.Equal(first.RejectionReason, second.RejectionReason);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static Order Market(decimal quantity = 1m)
        => Order.CreateMarket(Id, InstrumentId, OrderSide.Buy, new Quantity(quantity), TimeInForce.GTC, Created);

    private static UtcInstant At(int ticks) => new(Created.Value.AddTicks(ticks));

    private static ExecutionId Execution(int number) => new(new Guid(number, 0, 0, new byte[8]));

    private static OrderFill Fill(int number, decimal quantity, decimal price = 10m)
        => new(Execution(number), Id, new Quantity(quantity), new Price(price), Created);

    private static Order InState(OrderStatus state)
    {
        var order = Market();
        if (state == OrderStatus.Created)
        {
            return order;
        }

        if (state == OrderStatus.Rejected)
        {
            order.Reject(OrderRejectionReason.PreTradeValidationFailed, At(1));
            return order;
        }

        order.Accept(At(1));
        if (state == OrderStatus.Accepted)
        {
            return order;
        }

        order.ApplyFill(Fill(1, state == OrderStatus.Filled ? 1m : 0.25m), At(2));
        if (state == OrderStatus.Cancelled)
        {
            order.Cancel(At(3));
        }

        return order;
    }

    private static void Operate(Order order, string operation, UtcInstant? at)
    {
        switch (operation)
        {
            case "Accept": order.Accept(at); break;
            case "PreTradeReject": order.Reject(OrderRejectionReason.PreTradeValidationFailed, at); break;
            case "ProviderReject": order.Reject(OrderRejectionReason.ExecutionProviderRejected, at); break;
            case "Cancel": order.Cancel(at); break;
            case "Fill": order.ApplyFill(Fill(99, 0.25m), at); break;
            default: throw new ArgumentException("Unknown test operation.");
        }
    }

    private static void UnchangedFailure(Order order, Action operation)
    {
        var status = order.Status;
        var at = order.LastUpdatedAt;
        var reason = order.RejectionReason;
        var fills = order.Fills.ToArray();
        var filled = order.FilledQuantity;
        var remaining = order.RemainingQuantity;
        var completed = order.CompletedAt;
        Assert.Throws<DomainException>(operation);
        Assert.Equal(status, order.Status);
        Assert.Same(at, order.LastUpdatedAt);
        Assert.Equal(reason, order.RejectionReason);
        Assert.Equal(fills, order.Fills);
        Assert.Equal(filled, order.FilledQuantity);
        Assert.Equal(remaining, order.RemainingQuantity);
        Assert.Same(completed, order.CompletedAt);
    }
}
