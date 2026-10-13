using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Tests;

public sealed class B3SessionTests
{
    [Fact]
    public void RegularStateStaysOpenOnlyInsideTheWindow()
    {
        var start = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

        Assert.True(B3Session.IsOpen("REGULAR", start, end, start));
        Assert.True(B3Session.IsOpen("regular", start, end, start.AddHours(2)));
        Assert.False(B3Session.IsOpen("REGULAR", start, end, end));
        Assert.False(B3Session.IsOpen("CLOSED", start, end, start.AddHours(1)));
        Assert.False(B3Session.IsOpen("POST", start, end, start.AddHours(1)));
        Assert.False(B3Session.IsOpen("PRE", start, end, start.AddMinutes(-5)));
    }

    [Fact]
    public void AMissingStateUsesTheWeekdayCashHours()
    {
        var wednesdayNoon = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero);
        var saturdayNoon = new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);
        var wednesdayEvening = new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

        Assert.Equal(DayOfWeek.Wednesday, B3Session.SaoPauloDate(wednesdayNoon).DayOfWeek);
        Assert.True(B3Session.IsOpen(null, null, null, wednesdayNoon));
        Assert.False(B3Session.IsOpen("  ", null, null, saturdayNoon));
        Assert.False(B3Session.IsOpen(null, null, null, wednesdayEvening));
    }

    [Fact]
    public void BarsOutsideTheCashWindowAreDropped()
    {
        var start = new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);
        var inside = Bar(start.AddHours(1), 10m);
        var after = Bar(end.AddHours(1), 11m);
        var now = start.AddHours(2);

        var clipped = B3Session.ClipToCashSession([after, inside], start, end, now);

        var kept = Assert.Single(clipped);
        Assert.Equal(inside.OpenTime, kept.OpenTime);
    }

    [Fact]
    public void AnOpenWindowWithoutBarsKeepsThePreviousCashSession()
    {
        var start = new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(7);
        var during = Bar(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero), 10m);
        var afterClose = Bar(new DateTimeOffset(2026, 10, 13, 21, 0, 0, TimeSpan.Zero), 12m);

        var clipped = B3Session.ClipToCashSession([afterClose, during], start, end, start.AddMinutes(20));

        var kept = Assert.Single(clipped);
        Assert.Equal(during.OpenTime, kept.OpenTime);
    }

    [Fact]
    public void TwoCashSessionsStayAndTheOvernightIsDropped()
    {
        var monday = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        var mondayNight = new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.Zero);
        var tuesday = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);

        var clipped = B3Session.ClipToCashSession(
            [Bar(mondayNight, 99m), Bar(tuesday, 11m), Bar(monday, 10m)],
            start,
            start.AddHours(7),
            start.AddHours(2));

        Assert.Equal(2, clipped.Count);
        Assert.Equal(monday, clipped[0].OpenTime.Value);
        Assert.Equal(tuesday, clipped[1].OpenTime.Value);
    }

    [Fact]
    public void BeforeTheNextOpenThePreviousCashSessionRemains()
    {
        var nextOpen = new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero);
        var nextClose = nextOpen.AddHours(7);
        var during = Bar(new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero), 10m);
        var afterClose = Bar(new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero), 12m);
        var now = new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero);

        var clipped = B3Session.ClipToCashSession([afterClose, during], nextOpen, nextClose, now);

        var kept = Assert.Single(clipped);
        Assert.Equal(during.OpenTime, kept.OpenTime);
    }

    private static SessionBar Bar(DateTimeOffset open, decimal price) =>
        new(new UtcInstant(open), price, price, price, price, 1m);
}
