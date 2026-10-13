namespace VortexTrading.Infrastructure.Persistence;

/// <summary>One stored minute of a catalog instrument. The chart aggregates these onto the formation clock.</summary>
internal sealed class MinuteBarRow
{
    public Guid InstrumentId { get; set; }

    public DateTimeOffset OpenTime { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public decimal Volume { get; set; }
}
