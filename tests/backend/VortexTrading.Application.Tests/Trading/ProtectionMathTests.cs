using VortexTrading.Application.Trading;

namespace VortexTrading.Application.Tests.Trading;

public sealed class ProtectionMathTests
{
    private static readonly ProtectionSpec Points = new(10m, 5m, ProtectionUnit.Points, null, null);

    [Fact]
    public void LongBracketSitsAboveAndBelowTheAverage()
    {
        var (target, stop) = ProtectionMath.Levels(100m, 2m, Points, 0.01m, 2);

        Assert.Equal(110m, target);
        Assert.Equal(95m, stop);
    }

    [Fact]
    public void ShortBracketMirrorsTheLong()
    {
        var (target, stop) = ProtectionMath.Levels(100m, -2m, Points, 0.01m, 2);

        Assert.Equal(90m, target);
        Assert.Equal(105m, stop);
    }

    [Fact]
    public void TicksAndPercentUseTheTickAndTheAnchor()
    {
        var ticks = ProtectionMath.Levels(208785m, 1m, new ProtectionSpec(2m, 1m, ProtectionUnit.Ticks, null, null), 5m, 0);
        Assert.Equal(208795m, ticks.Target);
        Assert.Equal(208780m, ticks.Stop);

        var percent = ProtectionMath.Levels(100m, 1m, new ProtectionSpec(1m, 0.5m, ProtectionUnit.Percent, null, null), 0.01m, 2);
        Assert.Equal(101m, percent.Target);
        Assert.Equal(99.5m, percent.Stop);
    }

    [Fact]
    public void TrailOnlyMovesInFavorAndBreakEvenWaitsForTheTrigger()
    {
        Assert.Equal(105m, ProtectionMath.TrailedStop(110m, 95m, 2m, 5m, 0.01m, 2));
        Assert.Null(ProtectionMath.TrailedStop(100m, 95m, 2m, 5m, 0.01m, 2));
        Assert.Null(ProtectionMath.BreakEvenStop(107m, 100m, 90m, 2m, 8m, 0.01m, 2));
        Assert.Equal(100m, ProtectionMath.BreakEvenStop(108m, 100m, 90m, 2m, 8m, 0.01m, 2));
    }
}
