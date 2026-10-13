using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>One instrument's minute bars, oldest first.</summary>
public sealed record StoredMinuteBars(InstrumentId InstrumentId, IReadOnlyList<SessionBar> Bars);

/// <summary>Durable minute tape for the catalog shown in the market watch.</summary>
public interface IMinuteBarStore
{
    /// <summary>Inserts or updates the supplied minutes. Other instruments and older minutes stay.</summary>
    Task SaveAsync(IReadOnlyList<StoredMinuteBars> batches, CancellationToken cancellationToken = default);

    /// <summary>Loads the recent cash-session minutes of every stored instrument.</summary>
    Task<IReadOnlyList<StoredMinuteBars>> LoadAsync(CancellationToken cancellationToken = default);
}
