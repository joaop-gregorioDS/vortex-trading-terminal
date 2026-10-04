namespace VortexTrading.Application.IndicatorConsumption;

/// <summary>Typed mathematical configuration; validity is determined by the frozen indicator constructor.</summary>
public abstract record IndicatorSpecification
{
    private protected IndicatorSpecification() { }

    public abstract string MachineId { get; }
}

public sealed record SmaSpecification : IndicatorSpecification
{
    public override string MachineId => "sma";
    public int Period { get; }

    public SmaSpecification(int period) => Period = period;
}

public sealed record EmaSpecification : IndicatorSpecification
{
    public override string MachineId => "ema";
    public int Period { get; }

    public EmaSpecification(int period) => Period = period;
}

public sealed record MacdSpecification : IndicatorSpecification
{
    public override string MachineId => "macd";
    public int FastPeriod { get; }
    public int SlowPeriod { get; }
    public int SignalPeriod { get; }

    public MacdSpecification(int fastPeriod, int slowPeriod, int signalPeriod)
    {
        FastPeriod = fastPeriod;
        SlowPeriod = slowPeriod;
        SignalPeriod = signalPeriod;
    }
}

public sealed record RsiSpecification : IndicatorSpecification
{
    public override string MachineId => "rsi";
    public int Period { get; }

    public RsiSpecification(int period) => Period = period;
}

public sealed record StochasticSpecification : IndicatorSpecification
{
    public override string MachineId => "slow-stochastic";
    public int KPeriod { get; }
    public int KSmoothingPeriod { get; }
    public int DPeriod { get; }

    public StochasticSpecification(int kPeriod, int kSmoothingPeriod, int dPeriod)
    {
        KPeriod = kPeriod;
        KSmoothingPeriod = kSmoothingPeriod;
        DPeriod = dPeriod;
    }
}

public sealed record BollingerBandsSpecification : IndicatorSpecification
{
    public override string MachineId => "bollinger-bands";
    public int Period { get; }
    public decimal StandardDeviationMultiplier { get; }

    public BollingerBandsSpecification(int period, decimal standardDeviationMultiplier)
    {
        Period = period;
        StandardDeviationMultiplier = standardDeviationMultiplier;
    }
}

public sealed record AtrSpecification : IndicatorSpecification
{
    public override string MachineId => "atr";
    public int Period { get; }

    public AtrSpecification(int period) => Period = period;
}

public sealed record VwapSpecification : IndicatorSpecification
{
    public override string MachineId => "vwap";
}

public sealed record AdxSpecification : IndicatorSpecification
{
    public override string MachineId => "adx";
    public int Period { get; }

    public AdxSpecification(int period) => Period = period;
}

public sealed record ObvSpecification : IndicatorSpecification
{
    public override string MachineId => "obv";
}
