using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Infrastructure.Trading.Execution;

/// <summary>Reads the machine clock for the outer execution boundary.</summary>
/// <remarks>
/// Only the coordinator chooses explicit times (see <see cref="IApplicationClock"/>); providers must
/// never consult a clock, so this lives here instead of in Application and is never handed to a
/// provider. Tests substitute a controllable clock to keep scenarios deterministic.
/// </remarks>
public sealed class SystemApplicationClock : IApplicationClock
{
    /// <summary>Reads the current instant in UTC.</summary>
    public UtcInstant Now() => new(DateTimeOffset.UtcNow);
}
