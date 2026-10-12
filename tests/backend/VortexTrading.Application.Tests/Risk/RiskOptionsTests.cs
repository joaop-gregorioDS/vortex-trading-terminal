using VortexTrading.Application.Risk;

namespace VortexTrading.Application.Tests.Risk;

public sealed class RiskOptionsTests
{
    [Fact]
    public void DefaultOptionsHaveSensibleLimits()
    {
        var options = new RiskOptions();

        Assert.True(options.Enabled);
        Assert.Equal(1000m, options.MaxOrderQuantity);
        Assert.Equal(50000m, options.MaxOrderNotional);
        Assert.Equal(2000m, options.MaxPositionQuantity);
        Assert.True(options.RequireReferencePriceForMarketOrders);
        Assert.Equal("VortexTrading:Risk", RiskOptions.SectionName);
    }
}
