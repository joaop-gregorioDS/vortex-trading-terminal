using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.MarketData.Yahoo;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>How often the Yahoo session is read. The machine clock does not invent prices.</summary>
public sealed class MarketFeedOptions
{
    public const string SectionName = "MarketFeed";

    /// <summary>Wall-clock pause between synchronized reads. Defaults to 15 seconds.</summary>
    public int TickIntervalMilliseconds { get; set; } = 15_000;
}

/// <summary>
/// Publishes one Yahoo round for the whole catalog. Prices move only while the B3 cash session
/// is open. After the close, later reads do not change the tape.
/// </summary>
public sealed class YahooMarketFeed : BackgroundService
{
    private readonly PaperTradingSession _session;
    private readonly IYahooSessionSource _source;
    private readonly IMinuteBarStore _bars;
    private readonly MarketFeedOptions _options;
    private readonly ILogger<YahooMarketFeed> _logger;
    private readonly Dictionary<Guid, DateTimeOffset> _savedThrough = [];
    private bool _persistWarned;

    public YahooMarketFeed(
        PaperTradingSession session,
        IYahooSessionSource source,
        IMinuteBarStore bars,
        IOptions<MarketFeedOptions> options,
        ILogger<YahooMarketFeed> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _session = session;
        _source = source;
        _bars = bars;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(5_000, _options.TickIntervalMilliseconds);
        _logger.LogInformation(
            "Yahoo session feed started for {InstrumentCount} instruments, every {Interval} ms.",
            CandleAggregator.Catalog.Count, interval);

        await RestoreAsync(stoppingToken).ConfigureAwait(false);
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
            _logger.LogWarning(error, "Yahoo session poll failed.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
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
                _logger.LogWarning(error, "Yahoo session poll failed.");
            }
        }
    }

    /// <summary>One read. A failed read leaves the tape unchanged.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        MarketRound? round;
        try
        {
            round = await _source.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "Yahoo session was not read.");
            return;
        }

        if (round is null)
        {
            _logger.LogWarning("Yahoo session returned no reference quote. Prices stay where they are.");
            return;
        }

        var before = _session.MarketOpen;
        _session.ApplyMarketRound(round);
        await PersistTailAsync(round, cancellationToken).ConfigureAwait(false);
        var after = _session.MarketOpen;
        if (before != after && after is true)
        {
            _logger.LogInformation("B3 session is open. Prices follow Yahoo Finance.");
        }
        else if (before != after && after is false)
        {
            _logger.LogInformation("B3 session is closed. Prices stay on the last print.");
        }
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _bars.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored.Count == 0)
            {
                return;
            }

            _session.RestoreMinuteBars(stored.ToDictionary(batch => batch.InstrumentId, batch => batch.Bars));
            foreach (var batch in stored)
            {
                if (batch.Bars.Count > 0)
                {
                    _savedThrough[batch.InstrumentId.Value] = batch.Bars[^1].OpenTime.Value;
                }
            }

            _logger.LogInformation("Restored minute bars for {InstrumentCount} instruments.", stored.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Stored minute bars could not be restored. The chart fills from Yahoo.");
        }
    }

    private async Task PersistTailAsync(MarketRound round, CancellationToken cancellationToken)
    {
        var batches = new List<StoredMinuteBars>();
        foreach (var update in round.Instruments)
        {
            if (!CandleAggregator.TryGetProfile(update.Ticker, out var profile) || profile is null)
            {
                continue;
            }

            DateTimeOffset? through = _savedThrough.TryGetValue(profile.InstrumentId.Value, out var saved)
                ? saved
                : null;
            var tail = _session.BarsSince(profile.InstrumentId, through);
            if (tail.Count > 0)
            {
                batches.Add(new StoredMinuteBars(profile.InstrumentId, tail));
            }
        }

        if (batches.Count == 0)
        {
            return;
        }

        try
        {
            await _bars.SaveAsync(batches, cancellationToken).ConfigureAwait(false);
            foreach (var batch in batches)
            {
                _savedThrough[batch.InstrumentId.Value] = batch.Bars[^1].OpenTime.Value;
            }

            _persistWarned = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            if (!_persistWarned)
            {
                _logger.LogWarning(error, "Minute bars could not be stored. The chart keeps them in memory.");
                _persistWarned = true;
            }
        }
    }
}
