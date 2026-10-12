using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>
/// Re-reads the latest 1-minute chart close for instruments that have a live chart order.
/// The read happens outside the session lock. Nothing is written into the simulated tape.
/// </summary>
public sealed class ChartClosePoller : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly PaperTradingSession _session;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ChartClosePoller> _logger;

    public ChartClosePoller(PaperTradingSession session, IServiceScopeFactory scopes,
        ILogger<ChartClosePoller> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(logger);
        _session = session;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Chart close poll failed.");
            }
        }
    }

    /// <summary>One pass. Resolves the close source only when a chart order is live.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (LiveTickers(_session).Count == 0)
        {
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var closes = scope.ServiceProvider.GetRequiredService<IChartCloseSource>();
        await PollAsync(_session, closes, _logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One pass against a caller-supplied source, so a test does not open a network client.</summary>
    public static async Task PollAsync(PaperTradingSession session, IChartCloseSource closes, ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var ticker in LiveTickers(session))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChartSeriesClose? close;
            try
            {
                close = await closes.LatestMinuteAsync(ticker, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Chart close for {Ticker} was not read.", ticker);
                continue;
            }

            if (close is null || !CandleAggregator.TryGetProfile(ticker, out var profile) || profile is null)
            {
                continue;
            }

            var volume = close.Volume > 0m ? close.Volume : 1m;
            try
            {
                session.ObserveChart(profile.InstrumentId, close.OpenTime, close.Close, volume);
            }
            catch (ArgumentException error)
            {
                logger.LogWarning(error, "Chart close for {Ticker} was not applied.", ticker);
            }
        }
    }

    private static List<string> LiveTickers(PaperTradingSession session)
    {
        var tickers = new List<string>();
        foreach (var order in session.Orders)
        {
            if (order.Origin != OrderOrigin.Chart)
            {
                continue;
            }

            if (order.Status is not (OrderStatus.Accepted or OrderStatus.PartiallyFilled))
            {
                continue;
            }

            if (!CandleAggregator.TryGetProfile(order.InstrumentId, out var profile) || profile is null)
            {
                continue;
            }

            if (!tickers.Contains(profile.Ticker, StringComparer.OrdinalIgnoreCase))
            {
                tickers.Add(profile.Ticker);
            }
        }

        return tickers;
    }
}
