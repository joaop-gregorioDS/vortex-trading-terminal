namespace VortexTrading.Application.MarketData;

public sealed record MarketDataProviderDescriptor
{
    public string Key { get; }
    public string DisplayName { get; }
    public MarketDataCapabilities Capabilities { get; }

    public MarketDataProviderDescriptor(string? key, string? displayName, MarketDataCapabilities capabilities)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Provider key must not be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Provider display name must not be empty.", nameof(displayName));
        }

        const MarketDataCapabilities supported = MarketDataCapabilities.Quotes
            | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles;

        if (capabilities == MarketDataCapabilities.None || (capabilities & ~supported) != MarketDataCapabilities.None)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities), "Provider must advertise at least one known capability.");
        }

        Key = key.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Capabilities = capabilities;
    }
}
