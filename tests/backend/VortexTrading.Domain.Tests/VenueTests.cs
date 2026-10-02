using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Domain.Tests;

public sealed class VenueTests
{
    [Theory]
    [InlineData("B3")]
    [InlineData("NASDAQ")]
    [InlineData("NYSE")]
    [InlineData("CME")]
    [InlineData("BINANCE")]
    public void AcceptsVenueWithoutAnExchangeCatalog(string value)
    {
        var venue = new Venue(value);

        Assert.Equal(value, venue.Value);
        Assert.Equal(value, venue.ToString());
    }

    [Theory]
    [InlineData(" b3 ", "B3")]
    [InlineData("\tnasdaq\r\n", "NASDAQ")]
    [InlineData(" custom/venue ", "CUSTOM/VENUE")]
    public void TrimsAndNormalizesWithoutExchangeSpecificFormat(string input, string expected)
    {
        Assert.Equal(expected, new Venue(input).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RejectsMissingVenue(string? input)
    {
        Assert.Throws<DomainException>(() => new Venue(input));
    }

    [Fact]
    public void EqualityAndHashingUseNormalizedVenue()
    {
        var first = new Venue(" b3 ");
        var second = new Venue("B3");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new Venue("NASDAQ"));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    public void NormalizationDoesNotUseCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal("BINANCE", new Venue(" binance ").ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
