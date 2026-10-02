using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class AssetClassTests
{
    [Fact]
    public void SupportedClassificationSetMatchesTheInitialDomainContract()
    {
        var expected = new[]
        {
            AssetClass.Other, AssetClass.Equity, AssetClass.ETF,
            AssetClass.Future, AssetClass.Option, AssetClass.Forex,
            AssetClass.Crypto, AssetClass.Index, AssetClass.FixedIncome
        };

        Assert.Equal(expected, Enum.GetValues<AssetClass>());
    }

    [Theory]
    [InlineData(AssetClass.Equity)]
    [InlineData(AssetClass.ETF)]
    [InlineData(AssetClass.Future)]
    [InlineData(AssetClass.Option)]
    [InlineData(AssetClass.Forex)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.Index)]
    [InlineData(AssetClass.FixedIncome)]
    [InlineData(AssetClass.Other)]
    public void InstrumentAcceptsSupportedClassification(AssetClass assetClass)
    {
        Assert.Equal(assetClass, CreateInstrument(assetClass).AssetClass);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void InstrumentRejectsUndefinedClassification(int value)
    {
        Assert.Throws<DomainException>(() => CreateInstrument((AssetClass)value));
    }

    private static Instrument CreateInstrument(AssetClass assetClass) => new(
        InstrumentId.From(new Guid("0d319028-b047-4e71-bd7d-9f6983dd17a4")),
        new Symbol("TEST"), new Venue("CUSTOM"), assetClass,
        new Currency("USD"), new Price(0.01m), new Quantity(0.001m));
}
