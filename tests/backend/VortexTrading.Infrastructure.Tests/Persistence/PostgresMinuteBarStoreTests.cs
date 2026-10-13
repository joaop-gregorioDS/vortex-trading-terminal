using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Persistence;

/// <summary>The minute tape upserts by open time and reloads the catalog's bars.</summary>
public sealed class PostgresMinuteBarStoreTests(PersistenceTestFixture fixture) : IClassFixture<PersistenceTestFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavingTheSameMinuteUpdatesTheClose()
    {
        var store = new PostgresMinuteBarStore(fixture.CreateContextFactory());
        var open = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
        var first = new SessionBar(new UtcInstant(open), 10m, 11m, 9m, 10.5m, 3m);
        var second = new SessionBar(new UtcInstant(open.AddMinutes(1)), 10.5m, 12m, 10m, 11m, 4m);

        await store.SaveAsync([new StoredMinuteBars(fixture.TestInstrument, [first, second])], Token);
        await store.SaveAsync(
            [new StoredMinuteBars(fixture.TestInstrument, [second with { Close = 11.5m, High = 12.5m }])],
            Token);

        var loaded = await store.LoadAsync(Token);
        var mine = Assert.Single(loaded, batch => batch.InstrumentId == fixture.TestInstrument);
        Assert.Equal(2, mine.Bars.Count);
        Assert.Equal(10.5m, mine.Bars[0].Close);
        Assert.Equal(11.5m, mine.Bars[1].Close);
        Assert.Equal(12.5m, mine.Bars[1].High);
    }
}
