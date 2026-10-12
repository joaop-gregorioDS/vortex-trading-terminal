using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VortexTrading.Application.MarketData;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Cadence of the simulated market feed.</summary>
public sealed class MarketFeedOptions
{
    public const string SectionName = "MarketFeed";

    /// <summary>Wall-clock interval between observations. Defaults to one second.</summary>
    public int TickIntervalMilliseconds { get; set; } = 1000;

    /// <summary>Simulated time advanced after each round of prints. Defaults to 15 seconds.</summary>
    public int SimulatedStepSeconds { get; set; } = 15;
}

/// <summary>Publishes one deterministic print per catalog instrument on a timer.</summary>
/// <remarks>
/// The machine clock only decides how often a round happens. The price and the simulated instant
/// come from the path and the step, so the same tick index always prints the same price. Each round
/// stamps every instrument with the same instant, then moves that instant forward.
/// </remarks>
public sealed class SimulatedMarketFeed : BackgroundService
{
    private readonly PaperTradingSession _session;
    private readonly MarketFeedOptions _options;
    private readonly ILogger<SimulatedMarketFeed> _logger;

    public SimulatedMarketFeed(PaperTradingSession session, IOptions<MarketFeedOptions> options,
        ILogger<SimulatedMarketFeed> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _session = session;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(100, _options.TickIntervalMilliseconds);
        var step = TimeSpan.FromSeconds(Math.Max(1, _options.SimulatedStepSeconds));

        _logger.LogInformation(
            "Simulated market feed started for {InstrumentCount} instruments, step {StepSeconds}s, every {Interval} ms.",
            CandleAggregator.Catalog.Count, step.TotalSeconds, interval);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            _session.PublishFeedRound(step);
        }
    }
}
