using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Tests.Math;

/// <summary>
/// Proves the exact arithmetic that cost basis and P&amp;L depend on. The point is not convenience but
/// the two failure modes plain decimal hides: silent rounding and silent overflow.
/// </summary>
public sealed class ExactDecimalTests
{
    [Theory]
    [InlineData("10.5", "2", "21")]
    [InlineData("0.1", "0.2", "0.02")]
    [InlineData("100.25", "3", "300.75")]
    [InlineData("-4.5", "2", "-9")]
    public void MultiplyIsExactForRepresentableResults(string left, string right, string expected)
    {
        var product = ExactDecimal.Multiply(decimal.Parse(left, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(right, System.Globalization.CultureInfo.InvariantCulture), 18);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), product);
    }

    [Fact]
    public void MultiplyPreservesDigitsUpToTheRequestedScale()
    {
        // The value carries 27 fractional digits, so only a scale that can hold them keeps it intact.
        // Asking for 18 is a request to round, and rounding to 18 digits must lose those digits.
        var value = 1.000000000000000000000000001m;

        Assert.Equal(1.000000000000000000m, ExactDecimal.Multiply(value, 1m, 18));
        Assert.Equal(value, ExactDecimal.Multiply(value, 1m, 27));
        Assert.Equal(value, ExactDecimal.Multiply(value, 1m, ExactDecimal.MaximumScale));
    }

    [Fact]
    public void MultiplyHandlesValuesWhoseDigitProductWouldOverflowNaiveArithmetic()
    {
        // A price with 15 significant digits multiplied by a quantity with 7 more produces a 22-digit
        // coefficient. Multiplying the coefficients separately, as this helper does, keeps the result
        // exact where the same product computed digit-by-digit would lose the tail.
        var price = 123456789012345m;
        var quantity = 1000000m;

        Assert.Equal(123456789012345000000m, ExactDecimal.Multiply(price, quantity, 0));
    }

    [Fact]
    public void MultiplyRejectsResultsBeyondDecimalRangeInsteadOfOverflowing()
    {
        // The exact product is about 7.9e56, which no decimal can hold. It is rejected explicitly
        // rather than overflowing or being truncated to fit.
        Assert.Throws<DomainException>(() => ExactDecimal.Multiply(decimal.MaxValue, decimal.MaxValue, 4));

        // The same large operand against a partner that keeps the product representable stays exact.
        Assert.Equal(decimal.MaxValue, ExactDecimal.Multiply(decimal.MaxValue, 1m, 28));
    }

    [Theory]
    [InlineData("1", "2", "0.5")]
    [InlineData("10", "4", "2.5")]
    [InlineData("1", "3", "0.333333333333333333")]
    [InlineData("-7", "2", "-3.5")]
    public void DivideRoundsOnceAtTheRequestedScale(string dividend, string divisor, string expected)
    {
        var quotient = ExactDecimal.Divide(
            decimal.Parse(dividend, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture), 18);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), quotient);
    }

    [Fact]
    public void DivideRoundsHalfAwayFromZero()
    {
        // 0.125 and -0.125 rounded to two digits must follow one documented rule, not banker's
        // rounding and not truncation.
        Assert.Equal(0.13m, ExactDecimal.Divide(1m, 8m, 2));
        Assert.Equal(-0.13m, ExactDecimal.Divide(-1m, 8m, 2));

        // 0.1225 rounds down at two digits because it is below the midpoint.
        Assert.Equal(0.12m, ExactDecimal.Divide(49m, 400m, 2));
    }

    [Fact]
    public void DivideByZeroIsRejected()
    {
        Assert.Throws<DomainException>(() => ExactDecimal.Divide(1m, 0m, 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void InvalidScaleIsRejected(int scale)
    {
        Assert.Throws<DomainException>(() => ExactDecimal.Multiply(1m, 1m, scale));
        Assert.Throws<DomainException>(() => ExactDecimal.Divide(1m, 1m, scale));
    }

    [Fact]
    public void AddAndSubtractAreExact()
    {
        Assert.Equal(0.3m, ExactDecimal.Add(0.1m, 0.2m));
        Assert.Equal(10.000000000000000000000000002m,
            ExactDecimal.Add(1m, 9.000000000000000000000000002m));
        Assert.Equal(-0.1m, ExactDecimal.Subtract(0.1m, 0.2m));
        Assert.Equal(0.1m, ExactDecimal.Negate(-0.1m));
    }

    [Fact]
    public void AddRejectsUnrepresentableResults()
    {
        Assert.Throws<DomainException>(() => ExactDecimal.Add(decimal.MaxValue, decimal.MaxValue));
    }

    [Fact]
    public void SumOfProductsMatchesTermByTermRoundedSum()
    {
        // Weighted sum as used by cost basis: three partial fills of different sizes and prices.
        // 2 x 100.25 = 200.50, 3 x 100.50 = 301.50, 1.5 x 99.75 = 149.625, total 651.625.
        var terms = new (decimal, decimal)[] { (2m, 100.25m), (3m, 100.50m), (1.5m, 99.75m) };

        var sum = ExactDecimal.SumOfProducts(terms, 8);

        Assert.Equal(651.625m, sum);
        Assert.Equal(ExactDecimal.Add(ExactDecimal.Add(200.5m, 301.5m), 149.625m), sum);
    }

    [Fact]
    public void CompareUsesExactUnits()
    {
        // 1.10 and 1.1 are the same value with different scales; decimal equality already handles
        // that, and Compare must agree.
        Assert.Equal(0, ExactDecimal.Compare(1.10m, 1.1m));
        Assert.True(ExactDecimal.Compare(1.1000000000000000000000000001m, 1.1m) > 0);
    }

    [Fact]
    public void SmallestRepresentableResultsAreNotRejectedByScaleBookkeeping()
    {
        // 1/2 is exactly 0.5. At scale 28 it needs 28 digits, which would exceed the coefficient
        // limit; reducing trailing zeros is what keeps an exact result acceptable.
        Assert.Equal(0.5m, ExactDecimal.Divide(1m, 2m, ExactDecimal.MaximumScale));
        Assert.Equal(2m, ExactDecimal.Divide(4m, 2m, 28));
    }

    [Fact]
    public void PlainStringRenderingKeepsEveryDigit()
    {
        Assert.Equal("1.0000000000000000000000000001",
            ExactDecimal.ToPlainString(1.0000000000000000000000000001m));
        Assert.Equal(28, ExactDecimal.ScaleOf(1.0000000000000000000000000001m));
    }
}
