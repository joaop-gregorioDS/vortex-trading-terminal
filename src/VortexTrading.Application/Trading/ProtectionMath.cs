using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common;

namespace VortexTrading.Application.Trading;

/// <summary>How a protection distance is measured from the average entry.</summary>
public enum ProtectionUnit
{
    Ticks,
    Points,
    Percent
}

/// <summary>Take-profit and stop distances supplied with an entry. The server turns them into prices.</summary>
public readonly record struct ProtectionSpec(
    decimal Gain,
    decimal Loss,
    ProtectionUnit Unit,
    decimal? Trail,
    decimal? BreakEven)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Unit))
        {
            throw new ArgumentOutOfRangeException(nameof(Unit));
        }

        if (Gain <= 0m || Loss <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(Gain), "Gain and loss must be positive.");
        }

        if (Trail is <= 0m || BreakEven is <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(Trail), "Trail and break-even must be positive when set.");
        }
    }
}

/// <summary>
/// Prices for a protective bracket. A long exits with a sell above the average and a stop below it;
/// a short is the mirror. Rounding uses the caller's tick.
/// </summary>
public static class ProtectionMath
{
    public static decimal Distance(decimal anchor, decimal amount, ProtectionUnit unit, decimal tickSize, int decimals)
    {
        if (anchor <= 0m || amount <= 0m || tickSize <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Protection distances need a positive anchor, amount and tick.");
        }

        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }

        var scale = Math.Min(Math.Max(decimals, 0) + 6, ExactDecimal.MaximumScale);
        return unit switch
        {
            ProtectionUnit.Ticks => ExactDecimal.Multiply(amount, tickSize, scale),
            ProtectionUnit.Points => amount,
            ProtectionUnit.Percent => ExactDecimal.Divide(
                ExactDecimal.Multiply(anchor, amount, scale), 100m, scale),
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    /// <summary>Target and stop prices, or null when rounding would put that leg on the wrong side of the average.</summary>
    public static (decimal? Target, decimal? Stop) Levels(
        decimal anchor, decimal signedQuantity, ProtectionSpec spec, decimal tickSize, int decimals)
    {
        spec.Validate();
        if (signedQuantity == 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(signedQuantity));
        }

        var gain = Distance(anchor, spec.Gain, spec.Unit, tickSize, decimals);
        var loss = Distance(anchor, spec.Loss, spec.Unit, tickSize, decimals);
        var targetRaw = signedQuantity > 0m ? anchor + gain : anchor - gain;
        var stopRaw = signedQuantity > 0m ? anchor - loss : anchor + loss;
        decimal? target = OnProfitSide(anchor, signedQuantity, targetRaw, tickSize, decimals, profit: true);
        decimal? stop = OnProfitSide(anchor, signedQuantity, stopRaw, tickSize, decimals, profit: false);
        return (target, stop);
    }

    /// <summary>Next stop, only when it moves in favor and stays on the stop side of the reference.</summary>
    public static decimal? TrailedStop(
        decimal reference, decimal currentStop, decimal signedQuantity, decimal trailDistance, decimal tickSize, int decimals)
    {
        if (reference <= 0m || currentStop <= 0m || trailDistance <= 0m || signedQuantity == 0m)
        {
            return null;
        }

        var raw = signedQuantity > 0m ? reference - trailDistance : reference + trailDistance;
        var desired = CandleAggregator.RoundToTick(raw, tickSize, decimals);
        if (desired <= 0m)
        {
            return null;
        }

        var improves = signedQuantity > 0m ? desired > currentStop : desired < currentStop;
        var staysStop = signedQuantity > 0m ? desired < reference : desired > reference;
        return improves && staysStop ? desired : null;
    }

    /// <summary>Stop at the average once the reference has moved the trigger distance in favor.</summary>
    public static decimal? BreakEvenStop(
        decimal reference, decimal average, decimal currentStop, decimal signedQuantity, decimal trigger, decimal tickSize, int decimals)
    {
        if (reference <= 0m || average <= 0m || currentStop <= 0m || trigger <= 0m || signedQuantity == 0m)
        {
            return null;
        }

        var favorable = signedQuantity > 0m ? reference - average : average - reference;
        if (favorable < trigger)
        {
            return null;
        }

        var entry = CandleAggregator.RoundToTick(average, tickSize, decimals);
        if (entry <= 0m)
        {
            return null;
        }

        var improves = signedQuantity > 0m ? entry > currentStop : entry < currentStop;
        var staysStop = signedQuantity > 0m ? entry < reference : entry > reference;
        return improves && staysStop ? entry : null;
    }

    private static decimal? OnProfitSide(
        decimal anchor, decimal signedQuantity, decimal raw, decimal tickSize, int decimals, bool profit)
    {
        var price = CandleAggregator.RoundToTick(raw, tickSize, decimals);
        if (price <= 0m)
        {
            return null;
        }

        var above = price > anchor;
        var longSide = signedQuantity > 0m;
        var valid = profit ? above == longSide : above != longSide;
        return valid ? price : null;
    }
}
