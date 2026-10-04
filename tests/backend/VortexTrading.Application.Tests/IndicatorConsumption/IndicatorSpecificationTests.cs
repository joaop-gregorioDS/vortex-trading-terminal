using VortexTrading.Application.IndicatorConsumption;

namespace VortexTrading.Application.Tests.IndicatorConsumption;

public sealed class IndicatorSpecificationTests
{
    public static TheoryData<IndicatorSpecification, IndicatorSpecification, string> EquivalentConfigurations => new()
    {
        { new SmaSpecification(20), new SmaSpecification(20), "sma" },
        { new EmaSpecification(20), new EmaSpecification(20), "ema" },
        { new MacdSpecification(12, 26, 9), new MacdSpecification(12, 26, 9), "macd" },
        { new RsiSpecification(14), new RsiSpecification(14), "rsi" },
        { new StochasticSpecification(14, 3, 3), new StochasticSpecification(14, 3, 3), "slow-stochastic" },
        { new BollingerBandsSpecification(20, 2m), new BollingerBandsSpecification(20, 2m), "bollinger-bands" },
        { new AtrSpecification(14), new AtrSpecification(14), "atr" },
        { new VwapSpecification(), new VwapSpecification(), "vwap" },
        { new AdxSpecification(14), new AdxSpecification(14), "adx" },
        { new ObvSpecification(), new ObvSpecification(), "obv" }
    };

    [Theory]
    [MemberData(nameof(EquivalentConfigurations))]
    public void EquivalentConfigurationsHaveStructuralEqualityHashingAndExactCanonicalIds(
        IndicatorSpecification first, IndicatorSpecification second, string machineId)
    {
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(StringComparer.Ordinal.Equals(machineId, first.MachineId));
        Assert.True(StringComparer.Ordinal.Equals(machineId, second.MachineId));
        Assert.False(StringComparer.Ordinal.Equals(machineId.ToUpperInvariant(), first.MachineId));
        Assert.Single(new HashSet<IndicatorSpecification> { first, second });
    }

    public static TheoryData<IndicatorSpecification, IndicatorSpecification> DifferentConfigurations => new()
    {
        { new SmaSpecification(20), new SmaSpecification(50) },
        { new SmaSpecification(20), new EmaSpecification(20) },
        { new EmaSpecification(9), new EmaSpecification(21) },
        { new RsiSpecification(14), new RsiSpecification(7) },
        { new AtrSpecification(14), new AtrSpecification(7) },
        { new AdxSpecification(14), new AdxSpecification(7) },
        { new MacdSpecification(12, 26, 9), new MacdSpecification(11, 26, 9) },
        { new MacdSpecification(12, 26, 9), new MacdSpecification(12, 27, 9) },
        { new MacdSpecification(12, 26, 9), new MacdSpecification(12, 26, 8) },
        { new StochasticSpecification(14, 3, 3), new StochasticSpecification(13, 3, 3) },
        { new StochasticSpecification(14, 3, 3), new StochasticSpecification(14, 2, 3) },
        { new StochasticSpecification(14, 3, 3), new StochasticSpecification(14, 3, 2) },
        { new BollingerBandsSpecification(20, 2m), new BollingerBandsSpecification(19, 2m) },
        { new BollingerBandsSpecification(20, 2m), new BollingerBandsSpecification(20, 2.000000000000000000000000001m) },
        { new VwapSpecification(), new ObvSpecification() }
    };

    [Theory]
    [MemberData(nameof(DifferentConfigurations))]
    public void TypeAndEveryMathematicalParameterContributeToConfigurationIdentity(
        IndicatorSpecification first, IndicatorSpecification second)
    {
        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
        Assert.Equal(2, new HashSet<IndicatorSpecification> { first, second }.Count);
    }

    [Fact]
    public void DecimalEqualityUsesNumericValueRatherThanTextScale()
    {
        var first = new BollingerBandsSpecification(20, 2m);
        var second = new BollingerBandsSpecification(20, 2.0000m);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ConstructorsPreserveTypedArgumentsWithoutDefaultsOrMathematicalValidation()
    {
        Assert.Equal(int.MinValue, new SmaSpecification(int.MinValue).Period);
        Assert.Equal(0, new EmaSpecification(0).Period);
        Assert.Equal(-1, new RsiSpecification(-1).Period);
        Assert.Equal(int.MaxValue, new AtrSpecification(int.MaxValue).Period);
        Assert.Equal(0, new AdxSpecification(0).Period);

        var macd = new MacdSpecification(26, 12, 0);
        Assert.Equal((26, 12, 0), (macd.FastPeriod, macd.SlowPeriod, macd.SignalPeriod));
        var stochastic = new StochasticSpecification(0, -3, int.MaxValue);
        Assert.Equal((0, -3, int.MaxValue), (stochastic.KPeriod, stochastic.KSmoothingPeriod, stochastic.DPeriod));
        var bollinger = new BollingerBandsSpecification(-1, decimal.MinValue);
        Assert.Equal(-1, bollinger.Period);
        Assert.Equal(decimal.MinValue, bollinger.StandardDeviationMultiplier);
    }

    [Fact]
    public void MultipleConfigurationsCoexistAndRemainIndependentOfObjectInstances()
    {
        var configurations = new Dictionary<IndicatorSpecification, string>
        {
            [new SmaSpecification(20)] = "short SMA",
            [new SmaSpecification(50)] = "long SMA",
            [new EmaSpecification(20)] = "EMA"
        };

        Assert.Equal(3, configurations.Count);
        Assert.Equal("short SMA", configurations[new SmaSpecification(20)]);
        Assert.Equal("long SMA", configurations[new SmaSpecification(50)]);
        Assert.Equal("EMA", configurations[new EmaSpecification(20)]);
    }
}
