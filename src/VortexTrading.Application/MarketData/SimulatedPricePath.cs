namespace VortexTrading.Application.MarketData;

/// <summary>
/// Deterministic price for one simulated print. The same profile and tick index always return the
/// same price, on that instrument's tick, with no clock and no random source.
/// </summary>
public static class SimulatedPricePath
{
    public static decimal PriceAt(AssetMarketProfile profile, long tickIndex)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (tickIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickIndex), "Tick index must not be negative.");
        }

        var amplitude = AmplitudeInTicks(profile);
        var offset = TriangleOffset(tickIndex, amplitude);
        var raw = profile.BasePrice + (offset * profile.TickSize);
        if (raw <= 0m)
        {
            raw = profile.TickSize;
        }

        return CandleAggregator.RoundToTick(raw, profile.TickSize, profile.Decimals);
    }

    private static int AmplitudeInTicks(AssetMarketProfile profile)
    {
        if (profile.TickSize <= 0m || profile.VolatilityFactor <= 0m)
        {
            return 1;
        }

        var ticks = decimal.Round(profile.VolatilityFactor / profile.TickSize, 0, MidpointRounding.AwayFromZero);
        if (ticks < 1m)
        {
            return 1;
        }

        if (ticks > 10_000m)
        {
            return 10_000;
        }

        return (int)ticks;
    }

    /// <summary>Walks from -amplitude to +amplitude and back, one tick at a time.</summary>
    private static int TriangleOffset(long tickIndex, int amplitude)
    {
        var span = amplitude * 2;
        var period = span * 2L;
        var position = tickIndex % period;
        if (position <= span)
        {
            return -amplitude + (int)position;
        }

        return amplitude - (int)(position - span);
    }
}
