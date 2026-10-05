using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests.Trading;

public sealed class OrderIdentityTests
{
    private static readonly Guid Supplied = new("e68ea44b-b074-4d16-92ce-fc9a4dc518a0");
    private static readonly Guid Different = new("17a2f0ad-4e63-4a09-b95e-3e5c3e618355");

    [Fact]
    public void OrderIdPreservesCallerIdentityWithValueEqualityAndStableHash()
    {
        var first = new OrderId(Supplied);
        var second = OrderId.From(Supplied);

        Assert.Equal(Supplied, first.Value);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, OrderId.From(Different));
        Assert.Single(new HashSet<OrderId> { first, second });
    }

    [Fact]
    public void ExecutionIdPreservesCallerIdentityWithValueEqualityAndStableHash()
    {
        var first = new ExecutionId(Supplied);
        var second = ExecutionId.From(Supplied);

        Assert.Equal(Supplied, first.Value);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, ExecutionId.From(Different));
        Assert.Single(new HashSet<ExecutionId> { first, second });
    }

    [Fact]
    public void EmptyIdsAreRejectedByConstructorAndFactory()
    {
        Assert.Throws<DomainException>(() => new OrderId(Guid.Empty));
        Assert.Throws<DomainException>(() => OrderId.From(Guid.Empty));
        Assert.Throws<DomainException>(() => new ExecutionId(Guid.Empty));
        Assert.Throws<DomainException>(() => ExecutionId.From(Guid.Empty));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    public void SuppliedIdentitiesHaveCultureIndependentStringRepresentation(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("e68ea44b-b074-4d16-92ce-fc9a4dc518a0", OrderId.From(Supplied).ToString());
            Assert.Equal("e68ea44b-b074-4d16-92ce-fc9a4dc518a0", ExecutionId.From(Supplied).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void EnumsContainExactlyTheApprovedSemanticValues()
    {
        Assert.Equal(["Buy", "Sell"], Enum.GetNames<OrderSide>());
        Assert.Equal(["Market", "Limit"], Enum.GetNames<OrderType>());
        Assert.Equal(["GTC"], Enum.GetNames<TimeInForce>());
        Assert.Equal(["Created", "Accepted", "PartiallyFilled", "Filled", "Cancelled", "Rejected"], Enum.GetNames<OrderStatus>());
        Assert.Equal(["PreTradeValidationFailed", "ExecutionProviderRejected"], Enum.GetNames<OrderRejectionReason>());
    }
}
