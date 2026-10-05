using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests.Trading;

public sealed class OrderFillTests
{
    private static readonly OrderId OrderId = new(new Guid("e68ea44b-b074-4d16-92ce-fc9a4dc518a0"));
    private static readonly ExecutionId ExecutionId = new(new Guid("17a2f0ad-4e63-4a09-b95e-3e5c3e618355"));
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    [Theory]
    [InlineData("ExecutionId")]
    [InlineData("OrderId")]
    [InlineData("Quantity")]
    [InlineData("Price")]
    [InlineData("ExecutedAt")]
    public void EveryRequiredComponentIsGuarded(string missing)
    {
        Assert.Throws<DomainException>(() => new OrderFill(
            missing == "ExecutionId" ? null : ExecutionId,
            missing == "OrderId" ? null : OrderId,
            missing == "Quantity" ? null : new Quantity(1m),
            missing == "Price" ? null : new Price(10m),
            missing == "ExecutedAt" ? null : Time));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZeroQuantityOrPriceIsRejectedWithoutChangingPrimitives(bool quantity)
    {
        Assert.Throws<DomainException>(() => new OrderFill(ExecutionId, OrderId,
            new Quantity(quantity ? 0m : 1m), new Price(quantity ? 10m : 0m), Time));
    }

    [Theory]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("0.12500")]
    [InlineData("79228162514264337593543950335")]
    public void PreservesSuppliedPositiveValuesAndUtcTicks(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var quantity = new Quantity(value);
        var price = new Price(value);
        var fill = new OrderFill(ExecutionId, OrderId, quantity, price, Time);

        Assert.Same(ExecutionId, fill.ExecutionId);
        Assert.Same(OrderId, fill.OrderId);
        Assert.Same(quantity, fill.Quantity);
        Assert.Same(price, fill.Price);
        Assert.Same(Time, fill.ExecutedAt);
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(fill.Quantity.Value));
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(fill.Price.Value));
        Assert.Equal(Time.Value.UtcTicks, fill.ExecutedAt.Value.UtcTicks);
    }

    [Fact]
    public void EqualityUsesValuesIncludingExecutionIdentityButNotDecimalScaleOrUtcOffset()
    {
        var first = new OrderFill(ExecutionId, OrderId, new Quantity(0.25m), new Price(10m), Time);
        var same = new OrderFill(new ExecutionId(ExecutionId.Value), new OrderId(OrderId.Value),
            new Quantity(0.2500m), new Price(10.000m), new UtcInstant(Time.Value.ToOffset(TimeSpan.FromHours(-3))));
        var different = new OrderFill(new ExecutionId(new Guid("00473f79-beb4-4eee-9cba-ad4b9e06f1d1")),
            OrderId, first.Quantity, first.Price, Time);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Theory]
    [InlineData("OrderId")]
    [InlineData("Quantity")]
    [InlineData("Price")]
    [InlineData("ExecutedAt")]
    public void DifferentEconomicContentWithTheSameExecutionIdIsUnequal(string changed)
    {
        var first = new OrderFill(ExecutionId, OrderId, new Quantity(1m), new Price(10m), Time);
        var second = new OrderFill(ExecutionId,
            changed == "OrderId" ? new OrderId(ExecutionId.Value) : OrderId,
            new Quantity(changed == "Quantity" ? 2m : 1m),
            new Price(changed == "Price" ? 11m : 10m),
            changed == "ExecutedAt" ? new UtcInstant(Time.Value.AddTicks(1)) : Time);

        Assert.NotEqual(first, second);
    }
}
