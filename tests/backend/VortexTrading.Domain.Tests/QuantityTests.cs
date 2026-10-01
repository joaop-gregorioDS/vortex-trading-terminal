using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class QuantityTests
{
    public static TheoryData<decimal> ValidValues => new()
    {
        1m,
        0m,
        0.125m,
        0.00000001m,
        7.1234567890123456789012345678m,
        1.2300m,
        decimal.MaxValue
    };

    public static TheoryData<decimal> NegativeValues => new()
    {
        -1m,
        -0.00000001m,
        decimal.MinValue
    };

    [Theory]
    [MemberData(nameof(ValidValues))]
    public void PreservesNonNegativeFractionalDecimal(decimal value)
    {
        var quantity = new Quantity(value);

        Assert.Equal(value, quantity.Value);
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(quantity.Value));
    }

    [Theory]
    [MemberData(nameof(NegativeValues))]
    public void RejectsNegativeQuantity(decimal value)
    {
        Assert.Throws<DomainException>(() => new Quantity(value));
    }

    [Fact]
    public void EqualityAndHashingUseNumericValue()
    {
        var first = new Quantity(0.125m);
        var second = new Quantity(0.1250m);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Quantity(0.126m));
    }
}
