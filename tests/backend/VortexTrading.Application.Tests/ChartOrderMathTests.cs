using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class ChartOrderMathTests
{
    [Theory]
    [InlineData(OrderSide.Buy, 100, 100, OrderType.Limit)]
    [InlineData(OrderSide.Buy, 99, 100, OrderType.Limit)]
    [InlineData(OrderSide.Buy, 101, 100, OrderType.Stop)]
    [InlineData(OrderSide.Sell, 100, 100, OrderType.Limit)]
    [InlineData(OrderSide.Sell, 101, 100, OrderType.Limit)]
    [InlineData(OrderSide.Sell, 99, 100, OrderType.Stop)]
    public void ClassifyUsesTheReferenceClose(OrderSide side, int price, int reference, OrderType expected) =>
        Assert.Equal(expected, ChartOrderMath.Classify(side, price, reference));

    [Fact]
    public void NotionalAppliesThePointValue()
    {
        Assert.Equal(41867m, ChartOrderMath.Notional(209335m, 1m, 0.20m));
        Assert.Equal(83734m, ChartOrderMath.Notional(209335m, 2m, 0.20m));
        Assert.Equal(49723m, ChartOrderMath.Notional(4972.3m, 1m, 10m));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void ClassificationRejectsANonPositivePrice(int price, int reference) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartOrderMath.Classify(OrderSide.Buy, price, reference));

    [Fact]
    public void ClassificationRejectsAnUndefinedSide() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartOrderMath.Classify((OrderSide)99, 10m, 10m));

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public void NotionalRejectsANonPositiveInput(int price, int quantity, int pointValue) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartOrderMath.Notional(price, quantity, pointValue));
}
