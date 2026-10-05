using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Supplies the current instant to components that are allowed to consult a clock.</summary>
/// <remarks>
/// The execution provider contract forbids an autonomous clock (ADR-033): command and application
/// times are always explicit inputs, which is what makes historical and paper execution reproducible.
/// A clock is legitimate only at the outer boundary that <em>chooses</em> those times — the
/// coordinator. Injecting it keeps that choice visible and testable instead of hiding a call to
/// <c>DateTimeOffset.UtcNow</c> inside coordination logic.
/// </remarks>
public interface IApplicationClock
{
    /// <summary>Returns the current instant in UTC.</summary>
    UtcInstant Now();
}
