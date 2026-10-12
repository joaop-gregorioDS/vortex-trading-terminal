using Microsoft.Extensions.Logging;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Infrastructure.Tests.Trading.Paper;

public sealed class ChartClosePollerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IdleSessionDoesNotReadTheSeries()
    {
        using var session = new PaperTradingSession();
        var source = new ScriptedCloses();

        await ChartClosePoller.PollAsync(session, source, SilentLogger.Instance, Token);

        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task SameBarDoesNotFillAndAZeroVolumeLaterBarFillsOne()
    {
        using var session = new PaperTradingSession();
        var petr = CandleAggregator.GetProfile("PETR4");
        var created = session.Now();
        var reference = new UtcInstant(created.Value.AddDays(8));
        var order = Order.CreateStop(OrderId.From(Guid.NewGuid()), petr.InstrumentId, OrderSide.Buy,
            new Quantity(5m), new Price(110m), TimeInForce.GTC, created);
        await session.SubmitAsync(order, reference, Token);
        Assert.Equal(OrderStatus.Accepted, order.Status);

        var source = new ScriptedCloses
        {
            Close = new ChartSeriesClose(130m, reference, 10m)
        };
        await ChartClosePoller.PollAsync(session, source, SilentLogger.Instance, Token);
        Assert.Equal(["PETR4"], source.Calls);
        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Empty(order.Fills);

        source.Close = new ChartSeriesClose(120m, new UtcInstant(reference.Value.AddMinutes(1)), 0m);
        await ChartClosePoller.PollAsync(session, source, SilentLogger.Instance, Token);

        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(1m, Assert.Single(order.Fills).Quantity.Value);
        Assert.Equal(120m, order.Fills[0].Price.Value);
        Assert.Null(session.CurrentTime);
        Assert.Equal(0m, session.ReadBook(petr.InstrumentId).AvailableVolume);
    }

    private sealed class ScriptedCloses : IChartCloseSource
    {
        public ChartSeriesClose? Close { get; set; }
        public List<string> Calls { get; } = [];

        public Task<ChartSeriesClose?> LatestMinuteAsync(string ticker, CancellationToken cancellationToken)
        {
            Calls.Add(ticker);
            return Task.FromResult(Close);
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public static readonly SilentLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
