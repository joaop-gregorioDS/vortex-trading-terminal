using System.Globalization;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class CultureInvariantTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    public void NormalizationAndStringRepresentationDoNotUseCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal("INSTRUMENT", new Symbol(" instrument ").ToString());
            Assert.Equal("IDR", new Currency(" idr ").ToString());
            Assert.Equal("-12.34 BRL", new Money(-12.34m, new Currency("BRL")).ToString());
            Assert.Equal("12.3400", new Price(12.3400m).ToString());
            Assert.Equal("0.125", new Quantity(0.125m).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
