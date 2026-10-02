using System.Globalization;
using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Markets;

public sealed record CandleInterval
{
    public TimeSpan Duration { get; }

    public static CandleInterval OneMinute { get; } = new(TimeSpan.FromMinutes(1));
    public static CandleInterval FiveMinutes { get; } = new(TimeSpan.FromMinutes(5));
    public static CandleInterval FifteenMinutes { get; } = new(TimeSpan.FromMinutes(15));
    public static CandleInterval ThirtyMinutes { get; } = new(TimeSpan.FromMinutes(30));
    public static CandleInterval OneHour { get; } = new(TimeSpan.FromHours(1));
    public static CandleInterval FourHours { get; } = new(TimeSpan.FromHours(4));
    public static CandleInterval OneDay { get; } = new(TimeSpan.FromDays(1));

    public CandleInterval(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("Candle interval duration must be positive.");
        }

        Duration = duration;
    }

    public override string ToString() => Duration.ToString("c", CultureInfo.InvariantCulture);
}
