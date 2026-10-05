using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Infrastructure.MarketData.Development;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Cadence and liquidity of the simulated market feed.</summary>
public sealed class MarketFeedOptions
{
    public const string SectionName = "MarketFeed";

    /// <summary>Wall-clock interval between observations. Defaults to one second.</summary>
    public int TickIntervalMilliseconds { get; set; } = 1000;

    /// <summary>Volume offered on every observation, which bounds how much can fill at once.</summary>
    public decimal TickVolume { get; set; } = 250m;

    /// <summary>Simulated minutes advanced per observation.</summary>
    public int SimulatedMinutesPerTick { get; set; } = 1;
}

/// <summary>Publishes simulated market observations into the trading session on a timer.</summary>
/// <remarks>
/// Prices come from the same deterministic generator that backs the demonstration chart, so the feed
/// is reproducible and no external provider, credentials or authorization is involved.
/// <para>
/// The generator's own timestamps are not reused. They are fixed in the past, and replaying them as-is
/// would move the simulation clock backwards, which the provider correctly refuses. Instead the feed
/// advances a simulated instant forward from the session's start and walks the generated price series
/// from the beginning, which keeps the causal rule intact while producing live-looking data.
/// </para>
/// <para>
/// The feed is the only source of simulated time. It never reads the machine clock to timestamp a
/// trading fact; the machine clock only decides how often a tick happens.
/// </para>
/// </remarks>
public sealed class SimulatedMarketFeed : BackgroundService
{
    private readonly PaperTradingSession _session;
    private readonly MarketFeedOptions _options;
    private readonly ILogger<SimulatedMarketFeed> _logger;
    private readonly IReadOnlyList<decimal> _prices;

    public SimulatedMarketFeed(PaperTradingSession session, IOptions<MarketFeedOptions> options,
        ILogger<SimulatedMarketFeed> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _session = session;
        _options = options.Value;
        _logger = logger;
        _prices = DevelopmentCandleDataset.Create().Events.Select(entry => entry.Candle!.Close.Value).ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(100, _options.TickIntervalMilliseconds);
        var volume = _options.TickVolume > 0 ? _options.TickVolume : 250m;
        var index = 0;
        var simulated = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

        _logger.LogInformation("Simulated market feed started with {TickCount} ticks every {Interval} ms.",
            _prices.Count, interval);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (index >= _prices.Count)
            {
                // The generated series is finite. Continuing past its end would require inventing
                // prices, so the feed stops and says so instead of continuing with fabricated data.
                _logger.LogInformation("Simulated market feed exhausted its {TickCount} generated prices.",
                    _prices.Count);
                return;
            }

            var price = _prices[index];
            _session.Publish(new UtcInstant(simulated), price, volume);
            index++;
            simulated = simulated.AddMinutes(Math.Max(1, _options.SimulatedMinutesPerTick));
        }
    }
}
