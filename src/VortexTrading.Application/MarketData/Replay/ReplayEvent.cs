using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>Contains exactly one original canonical payload for replay coordination.</summary>
public sealed class ReplayEvent
{
    public Quote? Quote { get; }
    public Trade? Trade { get; }
    public Candle? Candle { get; }

    public ReplayEventKind Kind => Quote is not null ? ReplayEventKind.Quote
        : Trade is not null ? ReplayEventKind.Trade : ReplayEventKind.Candle;

    public UtcInstant ReplayTime => Quote?.EventTime ?? Trade?.EventTime ?? Candle!.OpenTime;
    public InstrumentId InstrumentId => Quote?.InstrumentId ?? Trade?.InstrumentId ?? Candle!.InstrumentId;

    private ReplayEvent(Quote? quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        Quote = quote;
    }

    private ReplayEvent(Trade? trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Trade = trade;
    }

    private ReplayEvent(Candle? candle)
    {
        ArgumentNullException.ThrowIfNull(candle);
        Candle = candle;
    }

    public static ReplayEvent FromQuote(Quote? quote) => new(quote);
    public static ReplayEvent FromTrade(Trade? trade) => new(trade);
    public static ReplayEvent FromCandle(Candle? candle) => new(candle);
}
