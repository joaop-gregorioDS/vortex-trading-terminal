using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Accepts market observations for execution providers that match on supplied prices.</summary>
/// <remarks>
/// ADR-033 keeps <see cref="IExecutionProvider"/> free of market-data types, which is right for broker
/// adapters that own their pricing. A paper provider is the opposite case: it has no prices of its
/// own and must be told what the market is. Rather than widen the frozen execution boundary with
/// market types, this narrow Application port carries that capability, so the coordinator can drive a
/// paper provider without referencing Infrastructure.
/// <para>
/// Implementations own their matching rules. The observation time is the instant the price became
/// known and is the application time for any fact the observation produces.
/// </para>
/// </remarks>
public interface IPaperMarketObserver
{
    /// <summary>Publishes one observation and evaluates resting orders against it.</summary>
    /// <exception cref="ArgumentException">The observation is invalid or out of order.</exception>
    void Observe(PaperMarketObservation observation);
}
