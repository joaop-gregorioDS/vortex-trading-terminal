using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;
using PortfolioAggregate = VortexTrading.Domain.Portfolio.Portfolio;

namespace VortexTrading.Domain.Tests.Portfolio;

/// <summary>
/// Proves portfolio aggregation over positions: per-instrument separation, closed positions kept,
/// valuation strictly from supplied marks, and refusal to invent a price for an unmarked instrument.
/// </summary>
public sealed class PortfolioTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void EmptyPortfolioHasNothingAndNoUpdateTime()
    {
        var portfolio = PortfolioAggregate.Empty();

        Assert.Empty(portfolio.Positions);
        Assert.Null(portfolio.UpdatedAt);
        Assert.Empty(portfolio.OpenInstrumentIds);
        Assert.Equal(0m, portfolio.TotalCost);
        Assert.Equal(0m, portfolio.RealizedProfitAndLoss);
    }

    [Fact]
    public void FillsAreAggregatedPerInstrument()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(10m, 100m, 0), OrderSide.Buy).Portfolio
            .Apply(B, Fill(5m, 50m, 1), OrderSide.Buy).Portfolio
            .Apply(A, Fill(10m, 120m, 2), OrderSide.Buy).Portfolio;

        Assert.Equal(2, portfolio.Positions.Count);
        Assert.Equal(20m, portfolio.PositionFor(A).Quantity);
        Assert.Equal(2200m, portfolio.PositionFor(A).TotalCost);
        Assert.Equal(110m, portfolio.PositionFor(A).AverageCost);
        Assert.Equal(5m, portfolio.PositionFor(B).Quantity);
        Assert.Equal(250m, portfolio.PositionFor(B).TotalCost);
        Assert.Equal(2450m, portfolio.TotalCost);
        Assert.Equal(Minutes(2), portfolio.UpdatedAt);
        Assert.Equal(2, portfolio.OpenInstrumentIds.Count());
    }

    [Fact]
    public void InstrumentWithoutAPositionYieldsAnEmptyOne()
    {
        var position = PortfolioAggregate.Empty().PositionFor(B);

        Assert.Equal(B, position.InstrumentId);
        Assert.True(position.IsClosed);
        Assert.Equal(0m, position.TotalCost);
    }

    [Fact]
    public void ClosedPositionsAreKeptWithTheirRealizedResult()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(10m, 100m, 0), OrderSide.Buy).Portfolio
            .Apply(A, Fill(10m, 130m, 1), OrderSide.Sell).Portfolio;

        // The position stays for its history; it is simply no longer open.
        Assert.Single(portfolio.Positions);
        Assert.True(portfolio.PositionFor(A).IsClosed);
        Assert.Equal(300m, portfolio.PositionFor(A).RealizedProfitAndLoss);
        Assert.Equal(300m, portfolio.RealizedProfitAndLoss);
        Assert.Empty(portfolio.OpenInstrumentIds);
        Assert.Equal(0m, portfolio.TotalCost);
    }

    [Fact]
    public void RealizedResultsAccumulateAcrossInstruments()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(10m, 100m, 0), OrderSide.Buy).Portfolio
            .Apply(A, Fill(4m, 110m, 1), OrderSide.Sell).Portfolio
            .Apply(B, Fill(10m, 100m, 2), OrderSide.Buy).Portfolio
            .Apply(B, Fill(10m, 95m, 3), OrderSide.Sell).Portfolio;

        // 40 from A and -50 from B.
        Assert.Equal(-10m, portfolio.RealizedProfitAndLoss);
        Assert.Equal(40m, portfolio.PositionFor(A).RealizedProfitAndLoss);
        Assert.Equal(-50m, portfolio.PositionFor(B).RealizedProfitAndLoss);
    }

    [Fact]
    public void EquityAndUnrealizedResultUseOnlySuppliedMarks()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(10m, 100m, 0), OrderSide.Buy).Portfolio
            .Apply(B, Fill(5m, 50m, 1), OrderSide.Buy).Portfolio;

        var marks = new Dictionary<InstrumentId, Price> { [A] = new Price(120m) };

        // B has no mark, so it contributes nothing instead of being valued at cost.
        Assert.Equal(1200m, portfolio.Equity(marks));
        Assert.Equal(200m, portfolio.UnrealizedProfitAndLoss(marks));
        Assert.Equal(200m, portfolio.TotalProfitAndLoss(marks));

        // Cash reported by the caller is part of equity but not of profit and loss.
        Assert.Equal(2200m, portfolio.Equity(marks, cash: 1000m));
        Assert.Equal(200m, portfolio.TotalProfitAndLoss(marks));

        // With both marked, everything is included.
        var all = new Dictionary<InstrumentId, Price> { [A] = new Price(120m), [B] = new Price(60m) };
        Assert.Equal(1500m, portfolio.Equity(all));
        Assert.Equal(250m, portfolio.UnrealizedProfitAndLoss(all));
        Assert.Equal(250m, portfolio.TotalProfitAndLoss(all));
    }

    [Fact]
    public void UnmarkedOrClosedPositionsAreExcludedFromValuation()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(10m, 100m, 0), OrderSide.Buy).Portfolio
            .Apply(A, Fill(10m, 130m, 1), OrderSide.Sell).Portfolio;

        var marks = new Dictionary<InstrumentId, Price> { [A] = new Price(200m) };

        // A is closed, so the mark price must not resurrect its value.
        Assert.Equal(0m, portfolio.Equity(marks));
        Assert.Equal(0m, portfolio.UnrealizedProfitAndLoss(marks));
        Assert.Equal(300m, portfolio.TotalProfitAndLoss(marks));
    }

    [Fact]
    public void ExtremeMarkPriceIsRejectedInsteadOfRoundingSilently()
    {
        var portfolio = PortfolioAggregate.Empty()
            .Apply(A, Fill(1000000000000000000m, 1m, 0), OrderSide.Buy).Portfolio;
        var marks = new Dictionary<InstrumentId, Price> { [A] = new Price(decimal.MaxValue) };

        // The exact market value would exceed what a decimal can hold.
        Assert.Throws<DomainException>(() => portfolio.Equity(marks));
    }

    [Fact]
    public void TwoPositionsForOneInstrumentAreRejected()
    {
        var first = Position.Empty(A).Apply(Fill(1m, 10m, 0), OrderSide.Buy).Position;
        var second = Position.Empty(A).Apply(Fill(2m, 10m, 0), OrderSide.Buy).Position;

        Assert.Throws<DomainException>(() => PortfolioAggregate.From([first, second]));
    }

    [Fact]
    public void FromRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => PortfolioAggregate.From(null!));
        // A null element is an argument error, not a domain invariant violation.
        Assert.Throws<ArgumentNullException>(() => PortfolioAggregate.From([null!]));
        Assert.Throws<ArgumentNullException>(() => PortfolioAggregate.Empty().PositionFor(null!));
        Assert.Throws<ArgumentNullException>(() => PortfolioAggregate.Empty().Equity(null!));
        Assert.Throws<ArgumentNullException>(() => PortfolioAggregate.Empty().UnrealizedProfitAndLoss(null!));
    }

    [Fact]
    public void EqualityComparesHoldingsNotReferences()
    {
        var first = PortfolioAggregate.Empty().Apply(A, Fill(1m, 10m, 0), OrderSide.Buy).Portfolio;
        var second = PortfolioAggregate.Empty().Apply(A, Fill(1m, 10m, 0), OrderSide.Buy).Portfolio;
        var different = PortfolioAggregate.Empty().Apply(A, Fill(2m, 10m, 0), OrderSide.Buy).Portfolio;

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.NotEqual(first, different);
        Assert.NotEqual(first, PortfolioAggregate.Empty());
    }

    private static OrderFill Fill(decimal quantity, decimal price, int minuteOffset) =>
        new(ExecutionId.From(Guid.NewGuid()), OrderId.From(Guid.NewGuid()), new Quantity(quantity),
            new Price(price), Minutes(minuteOffset));

    private static UtcInstant Minutes(int offset) => new(Start.Value.AddMinutes(offset));
}
