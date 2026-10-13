namespace VortexTrading.Application.MarketData;

/// <summary>
/// Cash session of B3 in America/Sao_Paulo. The platform moves only while this session is open,
/// including proxies that would otherwise trade overnight.
/// </summary>
public static class B3Session
{
    public static readonly TimeOnly CashOpen = new(10, 0);
    public static readonly TimeOnly CashClose = new(17, 0);

    /// <summary>Brasília, fixed at UTC−3. Brazil does not observe daylight saving.</summary>
    public static TimeZoneInfo SaoPaulo { get; } = ResolveSaoPaulo();

    /// <summary>
    /// True when Yahoo reports the regular session and the clock is still inside that window.
    /// A missing state falls back to the weekday cash hours, so a holiday reported as closed stays shut.
    /// </summary>
    public static bool IsOpen(
        string? marketState,
        DateTimeOffset? regularStart,
        DateTimeOffset? regularEnd,
        DateTimeOffset utcNow)
    {
        var state = marketState?.Trim();
        if (string.Equals(state, "REGULAR", StringComparison.OrdinalIgnoreCase))
        {
            if (regularStart is { } start && regularEnd is { } end && end > start)
            {
                return utcNow >= start && utcNow < end;
            }

            return true;
        }

        if (!string.IsNullOrEmpty(state))
        {
            return false;
        }

        return IsWeekdayCashSession(utcNow);
    }

    /// <summary>Weekday window from 10:00 inclusive to 17:00 exclusive in São Paulo.</summary>
    public static bool IsWeekdayCashSession(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, SaoPaulo);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(local.DateTime);
        return time >= CashOpen && time < CashClose;
    }

    /// <summary>UTC bounds of the cash session on one São Paulo calendar day.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CashWindow(DateOnly saoPauloDate)
    {
        var start = ToUtc(saoPauloDate, CashOpen);
        var end = ToUtc(saoPauloDate, CashClose);
        return (start, end);
    }

    public static DateOnly SaoPauloDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SaoPaulo).DateTime);

    /// <summary>
    /// Keeps every weekday cash session in the series and drops overnight and weekend tails.
    /// A 15-minute chart then shows one pregão after another. The regular window still decides
    /// whether the platform is open; it does not discard the previous sessions.
    /// </summary>
    public static IReadOnlyList<SessionBar> ClipToCashSession(
        IReadOnlyList<SessionBar> bars,
        DateTimeOffset? regularStart,
        DateTimeOffset? regularEnd,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(bars);
        _ = regularStart;
        _ = regularEnd;
        _ = utcNow;
        if (bars.Count == 0)
        {
            return bars;
        }

        var cash = new List<SessionBar>(bars.Count);
        foreach (var bar in bars.OrderBy(bar => bar.OpenTime))
        {
            if (IsWeekdayCashSession(bar.OpenTime.Value))
            {
                cash.Add(bar);
            }
        }

        return cash;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, SaoPaulo.GetUtcOffset(local)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveSaoPaulo()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "America/Sao_Paulo", TimeSpan.FromHours(-3), "Brasilia", "Brasilia");
    }
}
