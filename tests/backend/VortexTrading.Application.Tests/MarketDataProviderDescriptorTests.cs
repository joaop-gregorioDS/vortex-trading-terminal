using System.Globalization;
using VortexTrading.Application.MarketData;

namespace VortexTrading.Application.Tests;

public sealed class MarketDataProviderDescriptorTests
{
    [Fact]
    public void ConstructionPreservesStableMetadataAndCapabilities()
    {
        var descriptor = new MarketDataProviderDescriptor("custom", "Custom Feed",
            MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades);

        Assert.Equal("custom", descriptor.Key);
        Assert.Equal("Custom Feed", descriptor.DisplayName);
        Assert.True(descriptor.Capabilities.HasFlag(MarketDataCapabilities.Quotes));
        Assert.True(descriptor.Capabilities.HasFlag(MarketDataCapabilities.Trades));
        Assert.False(descriptor.Capabilities.HasFlag(MarketDataCapabilities.Candles));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("\u00a0")]
    public void RejectsMissingOrBlankKey(string? key)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MarketDataProviderDescriptor(key, "Custom Feed", MarketDataCapabilities.Quotes));

        Assert.Equal("key", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("\u00a0")]
    public void RejectsMissingOrBlankDisplayName(string? displayName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MarketDataProviderDescriptor("custom", displayName, MarketDataCapabilities.Quotes));

        Assert.Equal("displayName", exception.ParamName);
    }

    [Theory]
    [InlineData("custom", "Custom Feed", "custom", "Custom Feed")]
    [InlineData("  CUSTOM-Key  ", "  Custom Feed  ", "custom-key", "Custom Feed")]
    [InlineData("\tCustom.Source\n", "\tMixed Case Feed\n", "custom.source", "Mixed Case Feed")]
    public void NormalizesKeyAndTrimsDisplayNameWithoutChangingItsCase(
        string key, string displayName, string expectedKey, string expectedDisplayName)
    {
        var descriptor = new MarketDataProviderDescriptor(key, displayName, MarketDataCapabilities.Quotes);

        Assert.Equal(expectedKey, descriptor.Key);
        Assert.Equal(expectedDisplayName, descriptor.DisplayName);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Quotes | MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Trades | MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles)]
    public void AcceptsEveryNonEmptyCombinationOfKnownCapabilities(MarketDataCapabilities capabilities)
    {
        var descriptor = new MarketDataProviderDescriptor("custom", "Custom Feed", capabilities);

        Assert.Equal(capabilities, descriptor.Capabilities);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(128)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void RejectsNoCapabilitiesAndUnknownFlagBits(int capabilities)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MarketDataProviderDescriptor("custom", "Custom Feed", (MarketDataCapabilities)capabilities));

        Assert.Equal("capabilities", exception.ParamName);
    }

    [Fact]
    public void EquivalentNormalizedDescriptorsHaveStructuralEqualityAndConsistentHashing()
    {
        var first = new MarketDataProviderDescriptor("CUSTOM", " Custom Feed ", MarketDataCapabilities.Quotes);
        var second = new MarketDataProviderDescriptor("custom", "Custom Feed", MarketDataCapabilities.Quotes);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<MarketDataProviderDescriptor> { first, second });
    }

    [Theory]
    [InlineData("Key")]
    [InlineData("DisplayName")]
    [InlineData("Capabilities")]
    public void DifferentDescriptorComponentsProduceUnequalValues(string component)
    {
        var first = new MarketDataProviderDescriptor("custom", "Custom Feed", MarketDataCapabilities.Quotes);
        var second = new MarketDataProviderDescriptor(
            component == "Key" ? "other" : "custom",
            component == "DisplayName" ? "Other Feed" : "Custom Feed",
            component == "Capabilities" ? MarketDataCapabilities.Trades : MarketDataCapabilities.Quotes);

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void KeyNormalizationAndEqualityAreCultureIndependent(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var expected = new MarketDataProviderDescriptor("custom-i", "Custom Feed", MarketDataCapabilities.Trades);

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var actual = new MarketDataProviderDescriptor(" CUSTOM-I ", " Custom Feed ", MarketDataCapabilities.Trades);

            Assert.Equal("custom-i", actual.Key);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
