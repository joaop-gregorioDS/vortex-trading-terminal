using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests.Portfolio;

/// <summary>
/// Proves average-cost accounting with exact arithmetic: cost basis, unchanged average after a sell,
/// realized and unrealized P&amp;L, and the overflow zone where plain decimal multiplication would
/// round or fail silently.
/// </summary>
public sealed class PositionTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BuyAccumulatesQuantityAndCostAndDerivesTheAverage()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(10m, 100m, 0), OrderSide.Buy).Position
            .Apply(Fill(10m, 120m, 1), OrderSide.Buy).Position;

        Assert.Equal(20m, position.Quantity);
        Assert.Equal(2200m, position.TotalCost);
        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(0m, position.RealizedProfitAndLoss);
        Assert.False(position.IsClosed);
        Assert.Equal(Minutes(1), position.UpdatedAt);
    }

    [Fact]
    public void SellRealizesTheDifferenceAgainstTheAverageAndKeepsItUnchanged()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(20m, 100m, 0), OrderSide.Buy).Position;

        var result = position.Apply(Fill(5m, 130m, 1), OrderSide.Sell);

        // Realized: (130 - 100) x 5 = 150. The remaining 15 units keep the 100 average.
        Assert.Equal(150m, result.Position.RealizedProfitAndLoss);
        Assert.Equal(15m, result.Position.Quantity);
        Assert.Equal(1500m, result.Position.TotalCost);
        Assert.Equal(100m, result.Position.AverageCost);
        Assert.Equal(5m, result.AppliedQuantity);
        Assert.Equal(0m, result.IgnoredQuantity);
        Assert.False(result.HadUnappliedQuantity);
    }

    [Fact]
    public void SellBelowAverageProducesNegativeRealizedResult()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(10m, 100m, 0), OrderSide.Buy).Position;

        var result = position.Apply(Fill(4m, 80m, 1), OrderSide.Sell);

        Assert.Equal(-80m, result.Position.RealizedProfitAndLoss);
        Assert.Equal(6m, result.Position.Quantity);
        Assert.Equal(600m, result.Position.TotalCost);
    }

    [Fact]
    public void ClosingThePositionLeavesNoCostRemainder()
    {
        // A third that never divides exactly would leave a residue if cost were tracked per unit.
        var position = Position.Empty(Instrument, moneyScale: 2)
            .Apply(Fill(3m, 10m, 0), OrderSide.Buy).Position;
        Assert.Equal(30m, position.TotalCost);

        var result = position.Apply(Fill(3m, 12m, 1), OrderSide.Sell);

        Assert.True(result.Position.IsClosed);
        Assert.Equal(0m, result.Position.Quantity);
        Assert.Equal(0m, result.Position.TotalCost);
        Assert.Equal(0m, result.Position.AverageCost);
        Assert.Equal(6m, result.Position.RealizedProfitAndLoss);
    }

    [Fact]
    public void SellingMoreThanHeldAppliesOnlyWhatExistsAndReportsTheExcess()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(5m, 100m, 0), OrderSide.Buy).Position;

        var result = position.Apply(Fill(8m, 110m, 1), OrderSide.Sell);

        Assert.Equal(5m, result.AppliedQuantity);
        Assert.Equal(3m, result.IgnoredQuantity);
        Assert.True(result.HadUnappliedQuantity);
        Assert.True(result.Position.IsClosed);
        Assert.Equal(50m, result.Position.RealizedProfitAndLoss);
    }

    [Fact]
    public void SellingAnEmptyPositionChangesNothing()
    {
        var empty = Position.Empty(Instrument);

        var result = empty.Apply(Fill(1m, 100m, 0), OrderSide.Sell);

        Assert.Same(empty, result.Position);
        Assert.Equal(0m, result.AppliedQuantity);
        Assert.Equal(1m, result.IgnoredQuantity);
    }

    [Fact]
    public void MultipleSellsAccumulateRealizedResult()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(10m, 100m, 0), OrderSide.Buy).Position
            .Apply(Fill(2m, 110m, 1), OrderSide.Sell).Position
            .Apply(Fill(3m, 90m, 2), OrderSide.Sell).Position;

        // (110-100)x2 = 20 and (90-100)x3 = -30, so the running realized result is -10.
        Assert.Equal(-10m, position.RealizedProfitAndLoss);
        Assert.Equal(5m, position.Quantity);
        Assert.Equal(500m, position.TotalCost);
    }

    [Fact]
    public void UnrealizedAndTotalResultUseTheMarkPrice()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(10m, 100m, 0), OrderSide.Buy).Position
            .Apply(Fill(4m, 120m, 1), OrderSide.Sell).Position;

        // Realized (120-100)x4 = 80; remaining 6 units cost 600 and at 130 are worth 780.
        Assert.Equal(80m, position.RealizedProfitAndLoss);
        Assert.Equal(180m, position.UnrealizedProfitAndLoss(new Price(130m)));
        Assert.Equal(260m, position.TotalProfitAndLoss(new Price(130m)));

        // A closed position has no unrealized outcome; everything is already realized.
        var closed = position.Apply(Fill(6m, 130m, 2), OrderSide.Sell).Position;
        Assert.True(closed.IsClosed);
        Assert.Equal(0m, closed.UnrealizedProfitAndLoss(new Price(999m)));
        Assert.Equal(260m, closed.RealizedProfitAndLoss);
    }

    [Fact]
    public void FractionalQuantitiesStayExact()
    {
        var position = Position.Empty(Instrument)
            .Apply(Fill(0.1m, 3m, 0), OrderSide.Buy).Position
            .Apply(Fill(0.2m, 3m, 1), OrderSide.Buy).Position;

        // The classic binary-floating-point trap: 0.1 + 0.2 must be exactly 0.3 here.
        Assert.Equal(0.3m, position.Quantity);
        Assert.Equal(0.9m, position.TotalCost);
        Assert.Equal(3m, position.AverageCost);
    }

    [Fact]
    public void HighPrecisionPricesKeepTheirDigits()
    {
        var price = 1.000000000000000000000000001m;
        var position = Position.Empty(Instrument, moneyScale: 27)
            .Apply(Fill(2m, price, 0), OrderSide.Buy).Position;

        Assert.Equal(2.000000000000000000000000002m, position.TotalCost);
        Assert.Equal(price, position.AverageCost);
    }

    [Fact]
    public void CostBeyondDecimalRangeIsRejectedInsteadOfRoundingSilently()
    {
        var position = Position.Empty(Instrument, moneyScale: 0);
        var enormous = Fill(1000000000000000000m, 1000000000000000000m, 0);

        // The exact cost is 1e36, which no decimal can hold.
        Assert.Throws<Common.DomainException>(() => position.Apply(enormous, OrderSide.Buy));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        var position = Position.Empty(Instrument);

        Assert.Throws<ArgumentNullException>(() => position.Apply(null, OrderSide.Buy));
        Assert.Throws<Common.DomainException>(() => position.Apply(Fill(1m, 1m, 0), (OrderSide)99));
        Assert.Throws<ArgumentNullException>(() => Position.Empty(null!));
        Assert.Throws<Common.DomainException>(() => Position.Empty(Instrument, moneyScale: 29));
        Assert.Throws<ArgumentNullException>(() => position.UnrealizedProfitAndLoss(null));
    }

    [Fact]
    public void EqualityUsesValueNotReference()
    {
        var first = Position.Empty(Instrument).Apply(Fill(1m, 10m, 0), OrderSide.Buy).Position;
        var second = Position.Empty(Instrument).Apply(Fill(1m, 10m, 0), OrderSide.Buy).Position;

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.NotEqual(first, Position.Empty(Instrument));
    }

    private static OrderFill Fill(decimal quantity, decimal price, int minuteOffset) =>
        new(ExecutionId.From(Guid.NewGuid()), OrderId.From(Guid.NewGuid()), new Quantity(quantity),
            new Price(price), Minutes(minuteOffset));

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));
}
