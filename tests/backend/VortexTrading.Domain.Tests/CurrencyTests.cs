using VortexTrading.Domain.Common;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("BRL", "BRL")]
    [InlineData(" usd ", "USD")]
    [InlineData("\teur\n", "EUR")]
    [InlineData("usdt", "USDT")]
    [InlineData("BTC", "BTC")]
    public void NormalizesCurrencyWithoutRequiringThreeCharacters(string input, string expected)
    {
        var currency = new Currency(input);

        Assert.Equal(expected, currency.Value);
        Assert.Equal(expected, currency.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RejectsMissingCurrency(string? input)
    {
        Assert.Throws<DomainException>(() => new Currency(input));
    }

    [Fact]
    public void EqualityAndHashingUseNormalizedCurrency()
    {
        var first = new Currency(" brl ");
        var second = new Currency("BRL");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Currency("USD"));
    }
}
