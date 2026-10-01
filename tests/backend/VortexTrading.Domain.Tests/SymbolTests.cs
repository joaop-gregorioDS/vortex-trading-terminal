using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Domain.Tests;

public sealed class SymbolTests
{
    [Theory]
    [InlineData("PETR4")]
    [InlineData("VALE3")]
    [InlineData("WIN")]
    [InlineData("AAPL")]
    [InlineData("BTCUSD")]
    [InlineData("BRK.B")]
    public void AcceptsSymbolsWithoutExchangeSpecificFormat(string value)
    {
        var symbol = new Symbol(value);

        Assert.Equal(value, symbol.Value);
        Assert.Equal(value, symbol.ToString());
    }

    [Theory]
    [InlineData(" petr4 ", "PETR4")]
    [InlineData("\tvale3\r\n", "VALE3")]
    [InlineData("btc/usd", "BTC/USD")]
    public void TrimsAndNormalizesCase(string input, string expected)
    {
        Assert.Equal(expected, new Symbol(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RejectsMissingIdentity(string? input)
    {
        Assert.Throws<DomainException>(() => new Symbol(input));
    }

    [Fact]
    public void EqualityAndHashingUseNormalizedIdentity()
    {
        var first = new Symbol(" petr4 ");
        var second = new Symbol("PETR4");

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Symbol("VALE3"));
    }
}
