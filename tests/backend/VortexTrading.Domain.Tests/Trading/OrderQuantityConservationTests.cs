using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests.Trading;

public sealed class OrderQuantityConservationTests
{
    private static readonly OrderId Id = new(new Guid("e68ea44b-b074-4d16-92ce-fc9a4dc518a0"));
    private static readonly InstrumentId InstrumentId = new(new Guid("9d0f6823-2e3b-40ef-a5ba-5d9e9b7179dc"));
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
    private const decimal Tiny = 0.0000000000000000000000000001m;
    private const decimal Large = 100000000000000000000m;

    [Fact]
    public void OD08RejectsAnAbsorbedIncomingFillWithoutAnyMutation()
    {
        var order = Accepted(Large + 1m);
        order.ApplyFill(Fill(1, Large), At(1));
        Assert.Equal(Large, checked(Large + Tiny));
        Assert.Equal(1m, order.RemainingQuantity.Value);
        UnchangedFailure(order, Fill(2, Tiny));
        Assert.Single(order.Fills);
        Assert.Equal(Large, order.FilledQuantity.Value);
        Assert.Equal(1m, order.RemainingQuantity.Value);
    }

    [Fact]
    public void ExactFilledSumDoesNotPermitAnInexactRemainingQuantity()
    {
        var order = Accepted(Large);
        Assert.Equal(Large, checked(Large - Tiny));
        UnchangedFailure(order, Fill(1, Tiny));
        Assert.Empty(order.Fills);
        Assert.Equal(0m, order.FilledQuantity.Value);
        Assert.Equal(Large, order.RemainingQuantity.Value);
    }

    [Fact]
    public void InexactRemainingAfterAPriorFillCannotPublishAnOtherwiseExactNewSum()
    {
        var order = Accepted(Large + 1m);
        order.ApplyFill(Fill(1, 1m), At(1));
        Assert.NotEqual(1m, 1m + Tiny);
        UnchangedFailure(order, Fill(2, Tiny));
        Assert.Single(order.Fills);
        Assert.Equal(1m, order.FilledQuantity.Value);
        Assert.Equal(Large, order.RemainingQuantity.Value);
    }

    [Fact]
    public void OverfillCannotBeHiddenByDecimalRoundingAtALargeMagnitude()
    {
        var order = Accepted(Large);
        order.ApplyFill(Fill(1, Large - 1m), At(1));
        const decimal incoming = 1.0000000000000001m;
        Assert.Equal(Large, checked((Large - 1m) + incoming));
        UnchangedFailure(order, Fill(2, incoming));
        Assert.Equal(1m, order.RemainingQuantity.Value);
    }

    [Fact]
    public void NearMaximumCoefficientPrecisionFailureIsAControlledDomainFailure()
    {
        var order = Accepted(decimal.MaxValue);
        order.ApplyFill(Fill(1, decimal.MaxValue - 1m), At(1));
        UnchangedFailure(order, Fill(2, 0.1m));
        Assert.Equal(decimal.MaxValue - 1m, order.FilledQuantity.Value);
        Assert.Equal(1m, order.RemainingQuantity.Value);
    }

    [Fact]
    public void QuantityBeyondTheDecimalMaximumFailsAsDomainOverfillBeforeConversion()
    {
        var order = Accepted(decimal.MaxValue);
        order.ApplyFill(Fill(1, decimal.MaxValue - 1m), At(1));
        UnchangedFailure(order, Fill(2, 2m));
    }

    public static TheoryData<decimal, decimal[]> ExactlyRepresentableSequences => new()
    {
        { 10.25m, [10m, 0.25m] },
        { 0.3m, [0.1m, 0.2m] },
        { 3.000125m, [1.0m, 2.000m, 0.000125m] },
        { 1m, [0.125m, 0.12500m, 0.25m, 0.5m] },
        { Tiny * 3m, [Tiny, Tiny, Tiny] },
        { 1m, [0.9999999999999999999999999999m, Tiny] },
        { decimal.MaxValue, [decimal.MaxValue - 1m, 1m] },
        { 7922816251426433759354395033.5m, [7922816251426433759354395033m, 0.5m] }
    };

    [Theory]
    [MemberData(nameof(ExactlyRepresentableSequences))]
    public void ValidMixedScalesConserveBothQuantitiesAndCompleteExactly(decimal total, decimal[] quantities)
    {
        var order = Accepted(total);
        var expected = 0m;
        for (var index = 0; index < quantities.Length; index++)
        {
            expected += quantities[index];
            Assert.True(order.ApplyFill(Fill(index + 1, quantities[index]), At(index + 1)));
            Assert.Equal(expected, order.FilledQuantity.Value);
            Assert.Equal(total - expected, order.RemainingQuantity.Value);
            Assert.Equal(index + 1 == quantities.Length ? OrderStatus.Filled : OrderStatus.PartiallyFilled, order.Status);
        }

        Assert.Equal(total, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(At(quantities.Length), order.CompletedAt);
    }

    public static TheoryData<decimal> RepresentableCoefficientAndScaleCases
    {
        get
        {
            var cases = new TheoryData<decimal>();
            for (byte scale = 0; scale <= 28; scale++)
            {
                // Cover every legal decimal scale with all three coefficient limbs populated,
                // including their sign bits, without relying on conversion through floating point.
                cases.Add(new decimal(-1, -1, -1, false, scale));
            }

            cases.Add(new decimal(int.MinValue, 0, 0, false, 0));
            cases.Add(new decimal(0, int.MinValue, 0, false, 14));
            cases.Add(new decimal(0, 0, int.MinValue, false, 28));
            cases.Add(10.250000m);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RepresentableCoefficientAndScaleCases))]
    public void ExactFullFillSupportsEveryDecimalScaleAndUnsignedCoefficientLimb(decimal total)
    {
        var order = Accepted(total);
        var fill = Fill(1, total);
        Assert.Equal(total, order.RemainingQuantity.Value);
        Assert.True(order.ApplyFill(fill, At(1)));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(total, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
        Assert.Equal(decimal.GetBits(total), decimal.GetBits(order.Quantity.Value));
        Assert.Equal(decimal.GetBits(total), decimal.GetBits(order.Fills[0].Quantity.Value));
    }

    [Fact]
    public void ExactArithmeticIsIndependentOfTheOrderOfRepresentableFills()
    {
        var first = Accepted(10.3m);
        var second = Accepted(10.3m);
        first.ApplyFill(Fill(1, 10m), At(1));
        first.ApplyFill(Fill(2, 0.1m), At(2));
        first.ApplyFill(Fill(3, 0.2m), At(3));
        second.ApplyFill(Fill(3, 0.2m), At(1));
        second.ApplyFill(Fill(2, 0.1m), At(2));
        second.ApplyFill(Fill(1, 10m), At(3));
        Assert.Equal(first.FilledQuantity, second.FilledQuantity);
        Assert.Equal(first.RemainingQuantity, second.RemainingQuantity);
        Assert.Equal(OrderStatus.Filled, first.Status);
        Assert.Equal(first.Status, second.Status);
    }

    [Fact]
    public void FailedExactnessDoesNotReserveExecutionIdentityOrPreventAValidRetry()
    {
        var order = Accepted(Large + 1m);
        order.ApplyFill(Fill(1, Large), At(1));
        UnchangedFailure(order, Fill(2, Tiny));
        Assert.True(order.ApplyFill(Fill(2, 1m), At(20)));
        Assert.Equal(2, order.Fills.Count);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(Large + 1m, order.FilledQuantity.Value);
        Assert.Equal(0m, order.RemainingQuantity.Value);
    }

    private static Order Accepted(decimal total)
    {
        var order = Order.CreateMarket(Id, InstrumentId, OrderSide.Buy, new Quantity(total), TimeInForce.GTC, Time);
        order.Accept(Time);
        return order;
    }

    private static OrderFill Fill(int number, decimal quantity)
        => new(new ExecutionId(new Guid(number, 0, 0, new byte[8])), Id, new Quantity(quantity), new Price(1m), Time);

    private static UtcInstant At(int ticks) => new(Time.Value.AddTicks(ticks));

    private static void UnchangedFailure(Order order, OrderFill fill)
    {
        var status = order.Status;
        var at = order.LastUpdatedAt;
        var reason = order.RejectionReason;
        var fills = order.Fills.ToArray();
        var filled = order.FilledQuantity;
        var remaining = order.RemainingQuantity;
        var completed = order.CompletedAt;
        Assert.Throws<DomainException>(() => order.ApplyFill(fill, At(20)));
        Assert.Equal(status, order.Status);
        Assert.Same(at, order.LastUpdatedAt);
        Assert.Equal(reason, order.RejectionReason);
        Assert.Equal(fills, order.Fills);
        Assert.Equal(filled, order.FilledQuantity);
        Assert.Equal(remaining, order.RemainingQuantity);
        Assert.Same(completed, order.CompletedAt);
    }
}
