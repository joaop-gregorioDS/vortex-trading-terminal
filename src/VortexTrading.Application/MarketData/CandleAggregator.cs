using System.Security.Cryptography;
using System.Text;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.MarketData;

/// <summary>
/// Market parameters for one simulated instrument. <see cref="PointValue"/> converts a price
/// difference into money: 1 for stocks, 0.20 for the mini-index and 10 for the mini-dollar.
/// </summary>
public sealed record AssetMarketProfile(
    string Ticker,
    InstrumentId InstrumentId,
    decimal BasePrice,
    decimal TickSize,
    int Decimals,
    decimal VolatilityFactor,
    decimal TypicalVolume,
    decimal PointValue = 1m)
{
    /// <summary>Quantity the simulated print offers to the matcher on each tick.</summary>
    public decimal PrintQuantity => TypicalVolume <= 0m
        ? 1m
        : decimal.Round(TypicalVolume / 100m, 0, MidpointRounding.AwayFromZero);
}

/// <summary>Closed catalog of simulated instruments and candle aggregation over their prints.</summary>
public static class CandleAggregator
{
    private static readonly Dictionary<string, AssetMarketProfile> Profiles = InitializeProfiles();

    private static readonly Dictionary<InstrumentId, AssetMarketProfile> ProfilesByInstrument =
        Profiles.Values.ToDictionary(profile => profile.InstrumentId);

    /// <summary>Instruments the session trades, in ticker order.</summary>
    public static IReadOnlyList<AssetMarketProfile> Catalog { get; } =
        Profiles.Values.OrderBy(profile => profile.Ticker, StringComparer.Ordinal).ToArray();

    /// <summary>Resolves a catalog ticker, or fails when the session does not trade it.</summary>
    public static bool TryGetProfile(string? ticker, out AssetMarketProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            profile = null;
            return false;
        }

        if (!Profiles.TryGetValue(ticker.Trim(), out var found))
        {
            profile = null;
            return false;
        }

        profile = found;
        return true;
    }

    /// <summary>Resolves a catalog instrument, or fails when the session does not trade it.</summary>
    public static bool TryGetProfile(InstrumentId? instrumentId, out AssetMarketProfile? profile)
    {
        if (instrumentId is null)
        {
            profile = null;
            return false;
        }

        if (!ProfilesByInstrument.TryGetValue(instrumentId, out var found))
        {
            profile = null;
            return false;
        }

        profile = found;
        return true;
    }

    public static AssetMarketProfile GetProfile(string ticker)
    {
        if (TryGetProfile(ticker, out var profile) && profile is not null)
        {
            return profile;
        }

        throw new ArgumentException("The ticker is not in the simulated catalog.", nameof(ticker));
    }

    public static decimal RoundToTick(decimal price, decimal tickSize, int decimals)
    {
        if (tickSize <= 0) return Math.Round(price, decimals, MidpointRounding.AwayFromZero);
        var ticks = Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero);
        return Math.Round(ticks * tickSize, decimals, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<string, AssetMarketProfile> InitializeProfiles()
    {
        var petrId = new InstrumentId(new Guid("11111111-1111-1111-1111-111111111111"));

        var dict = new Dictionary<string, AssetMarketProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["PETR4"] = new("PETR4", petrId, 54.84m, 0.01m, 2, 0.35m, 180000m),
            ["VALE3"] = new("VALE3", DeterministicId("VALE3"), 71.39m, 0.01m, 2, 0.45m, 120000m),
            ["ITUB4"] = new("ITUB4", DeterministicId("ITUB4"), 50.42m, 0.01m, 2, 0.30m, 89000m),
            ["BBDC4"] = new("BBDC4", DeterministicId("BBDC4"), 22.49m, 0.01m, 2, 0.18m, 65000m),
            ["BBAS3"] = new("BBAS3", DeterministicId("BBAS3"), 25.77m, 0.01m, 2, 0.22m, 43000m),
            ["ABEV3"] = new("ABEV3", DeterministicId("ABEV3"), 10.44m, 0.01m, 2, 0.10m, 31000m),
            ["MGLU3"] = new("MGLU3", DeterministicId("MGLU3"), 9.09m, 0.01m, 2, 0.12m, 78000m),
            ["RENT3"] = new("RENT3", DeterministicId("RENT3"), 51.74m, 0.01m, 2, 0.35m, 41000m),
            ["WEGE3"] = new("WEGE3", DeterministicId("WEGE3"), 51.14m, 0.01m, 2, 0.30m, 36000m),
            ["PRIO3"] = new("PRIO3", DeterministicId("PRIO3"), 83.16m, 0.01m, 2, 0.50m, 29000m),
            ["B3SA3"] = new("B3SA3", DeterministicId("B3SA3"), 23.72m, 0.01m, 2, 0.20m, 52000m),
            ["SUZB3"] = new("SUZB3", DeterministicId("SUZB3"), 42.43m, 0.01m, 2, 0.35m, 19000m),
            ["GGBR4"] = new("GGBR4", DeterministicId("GGBR4"), 28.10m, 0.01m, 2, 0.25m, 34000m),
            ["WING26"] = new("WING26", DeterministicId("WING26"), 208785m, 5m, 0, 55m, 48000m, 0.20m),
            ["WDOU26"] = new("WDOU26", DeterministicId("WDOU26"), 4957.0m, 0.5m, 1, 3.5m, 21000m, 10m),
        };

        return dict;
    }

    private static InstrumentId DeterministicId(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
        return new InstrumentId(new Guid(hash));
    }
}
