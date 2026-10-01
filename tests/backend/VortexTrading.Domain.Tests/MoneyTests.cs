using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class MoneyTests
{
    public static TheoryData<decimal> Amounts => new()
    {
        100m,
        0m,
        -12.5m,
        7.1234567890123456789012345678m,
        1.2300m,
        decimal.MinValue,
        decimal.MaxValue
    };

    [Theory]
    [MemberData(nameof(Amounts))]
    public void PreservesSignedAmountAndCurrency(decimal amount)
    {
        var currency = new Currency("BRL");
        var money = new Money(amount, currency);

        Assert.Equal(amount, money.Amount);
        Assert.Equal(decimal.GetBits(amount), decimal.GetBits(money.Amount));
        Assert.Equal(currency, money.Currency);
    }

    [Fact]
    public void RequiresCurrency()
    {
        Assert.Throws<DomainException>(() => new Money(1m, null));
    }

    [Fact]
    public void EqualityAndHashingIncludeAmountAndCurrency()
    {
        var first = new Money(100m, new Currency("brl"));
        var second = new Money(100.00m, new Currency(" BRL "));

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Money(101m, new Currency("BRL")));
        Assert.NotEqual(first, new Money(100m, new Currency("USD")));
    }

    [Fact]
    public void AddsSameCurrencyWithoutMutatingOperands()
    {
        var left = new Money(10.125m, new Currency("BRL"));
        var right = new Money(-0.025m, new Currency(" brl "));

        var sum = left + right;

        Assert.Equal(new Money(10.100m, new Currency("BRL")), sum);
        Assert.Equal(10.125m, left.Amount);
        Assert.Equal(-0.025m, right.Amount);
        Assert.NotSame(left, sum);
    }

    [Fact]
    public void SubtractsSameCurrencyAllowingNegativeResult()
    {
        var difference = new Money(1.125m, new Currency("USD"))
            - new Money(2.25m, new Currency("usd"));

        Assert.Equal(new Money(-1.125m, new Currency("USD")), difference);
    }

    [Fact]
    public void RejectsArithmeticAcrossCurrencies()
    {
        var brl = new Money(1m, new Currency("BRL"));
        var usd = new Money(1m, new Currency("USD"));

        Assert.Throws<DomainException>(() => brl + usd);
        Assert.Throws<DomainException>(() => brl - usd);
    }

    [Fact]
    public void RejectsNullArithmeticOperands()
    {
        var money = new Money(1m, new Currency("BRL"));

        Assert.Throws<DomainException>(() => money + null!);
        Assert.Throws<DomainException>(() => null! + money);
        Assert.Throws<DomainException>(() => money - null!);
        Assert.Throws<DomainException>(() => null! - money);
    }

    [Fact]
    public void DecimalOverflowFailsExplicitly()
    {
        var currency = new Currency("BRL");
        var one = new Money(1m, currency);

        Assert.Throws<OverflowException>(() => new Money(decimal.MaxValue, currency) + one);
        Assert.Throws<OverflowException>(() => new Money(decimal.MinValue, currency) - one);
    }
}
