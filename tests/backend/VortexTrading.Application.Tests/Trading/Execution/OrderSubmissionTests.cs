using System.Reflection;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Trading.Execution;

public sealed class OrderSubmissionTests
{
    private static readonly OrderId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId Instrument = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly Quantity Amount = new(0.00200m);
    private static readonly Price Limit = new(7.1234567890123456789012345678m);
    private static readonly UtcInstant Created = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Theory]
    [InlineData(OrderSide.Buy, OrderType.Market)]
    [InlineData(OrderSide.Sell, OrderType.Market)]
    [InlineData(OrderSide.Buy, OrderType.Limit)]
    [InlineData(OrderSide.Sell, OrderType.Limit)]
    public void PreservesEveryCanonicalIntentComponentWithoutConversion(OrderSide side, OrderType type)
    {
        var price = type == OrderType.Limit ? Limit : null;
        var submission = new OrderSubmission(Id, Instrument, side, type, Amount, price, TimeInForce.GTC, Created);

        Assert.Same(Id, submission.OrderId);
        Assert.Same(Instrument, submission.InstrumentId);
        Assert.Equal(side, submission.Side);
        Assert.Equal(type, submission.Type);
        Assert.Same(Amount, submission.Quantity);
        Assert.Same(price, submission.LimitPrice);
        Assert.Equal(TimeInForce.GTC, submission.TimeInForce);
        Assert.Same(Created, submission.CreatedAt);
    }

    [Theory]
    [InlineData("orderId")]
    [InlineData("instrumentId")]
    [InlineData("quantity")]
    [InlineData("createdAt")]
    public void RejectsMissingRequiredComponents(string parameter)
    {
        var error = Assert.Throws<ArgumentNullException>(() => new OrderSubmission(
            parameter == "orderId" ? null : Id, parameter == "instrumentId" ? null : Instrument,
            OrderSide.Buy, OrderType.Market, parameter == "quantity" ? null : Amount, null,
            TimeInForce.GTC, parameter == "createdAt" ? null : Created));

        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData("side", -1)]
    [InlineData("side", 2)]
    [InlineData("type", -1)]
    [InlineData("type", 2)]
    [InlineData("timeInForce", -1)]
    [InlineData("timeInForce", 1)]
    public void RejectsUndefinedIntentEnums(string parameter, int value)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new OrderSubmission(Id, Instrument,
            parameter == "side" ? (OrderSide)value : OrderSide.Buy,
            parameter == "type" ? (OrderType)value : OrderType.Market, Amount, null,
            parameter == "timeInForce" ? (TimeInForce)value : TimeInForce.GTC, Created));

        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MarketRejectsAnyLimitPriceIncludingZero(int value)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new OrderSubmission(Id, Instrument, OrderSide.Buy, OrderType.Market,
                Amount, new Price(value), TimeInForce.GTC, Created));

        Assert.Equal("limitPrice", error.ParamName);
    }

    [Fact]
    public void LimitRequiresAPriceInsteadOfUsingASentinel()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            new OrderSubmission(Id, Instrument, OrderSide.Buy, OrderType.Limit, Amount, null, TimeInForce.GTC, Created));

        Assert.Equal("limitPrice", error.ParamName);
    }

    [Fact]
    public void LimitRejectsZeroPrice()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderSubmission(Id, Instrument, OrderSide.Buy, OrderType.Limit,
                Amount, new Price(0m), TimeInForce.GTC, Created));

        Assert.Equal("limitPrice", error.ParamName);
    }

    [Fact]
    public void SubmissionRejectsZeroQuantityPermittedByTheGeneralPrimitive()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderSubmission(Id, Instrument, OrderSide.Buy, OrderType.Market,
                new Quantity(0m), null, TimeInForce.GTC, Created));

        Assert.Equal("quantity", error.ParamName);
    }

    [Fact]
    public void SnapshotRemainsDetachedWhenTheOriginalOrderChanges()
    {
        var order = Order.CreateLimit(Id, Instrument, OrderSide.Buy, Amount, Limit, TimeInForce.GTC, Created);
        var snapshot = new OrderSubmission(order.Id, order.InstrumentId, order.Side, order.Type,
            order.Quantity, order.LimitPrice, order.TimeInForce, order.CreatedAt);

        order.Accept(new UtcInstant(Created.Value.AddMinutes(1)));
        order.ApplyFill(new OrderFill(new ExecutionId(new Guid("33333333-3333-3333-3333-333333333333")),
            Id, new Quantity(0.001m), new Price(7m), Created), new UtcInstant(Created.Value.AddMinutes(2)));
        order.Cancel(new UtcInstant(Created.Value.AddMinutes(3)));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Same(Amount, snapshot.Quantity);
        Assert.Same(Limit, snapshot.LimitPrice);
        Assert.Same(Created, snapshot.CreatedAt);
        Assert.Equal(0.00200m, snapshot.Quantity.Value);
    }

    [Fact]
    public void PublicStateIsReadOnlyAndPrivateStateCannotRetainAnOrderOrCollection()
    {
        var type = typeof(OrderSubmission);
        Assert.True(type.IsSealed);
        Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field =>
        {
            Assert.True(field.IsInitOnly);
            Assert.NotEqual(typeof(Order), field.FieldType);
            Assert.False(typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType));
        });
        Assert.Equal(8, type.GetProperties().Length);
    }

    [Fact]
    public void IdenticalExplicitInputsHaveEquivalentObservableState()
    {
        var first = new OrderSubmission(Id, Instrument, OrderSide.Sell, OrderType.Limit, Amount, Limit, TimeInForce.GTC, Created);
        var second = new OrderSubmission(new OrderId(Id.Value), new InstrumentId(Instrument.Value),
            OrderSide.Sell, OrderType.Limit, new Quantity(Amount.Value), new Price(Limit.Value),
            TimeInForce.GTC, new UtcInstant(Created.Value));

        Assert.NotSame(first, second);
        Assert.All(typeof(OrderSubmission).GetProperties(), property =>
            Assert.Equal(property.GetValue(first), property.GetValue(second)));
    }
}
