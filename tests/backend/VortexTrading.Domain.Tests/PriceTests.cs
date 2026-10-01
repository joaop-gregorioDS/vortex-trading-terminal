using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class PriceTests
{
    public static TheoryData<decimal> ValidValues => new()
    {
        10m,
        0m,
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
    public void PreservesNonNegativeDecimalWithoutRounding(decimal value)
    {
        var price = new Price(value);

        Assert.Equal(value, price.Value);
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(price.Value));
    }

    [Theory]
    [MemberData(nameof(NegativeValues))]
    public void RejectsNegativePrice(decimal value)
    {
        Assert.Throws<DomainException>(() => new Price(value));
    }

    [Fact]
    public void EqualityAndHashingUseNumericValue()
    {
        var first = new Price(1.25m);
        var second = new Price(1.2500m);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Price(1.26m));
    }
}
