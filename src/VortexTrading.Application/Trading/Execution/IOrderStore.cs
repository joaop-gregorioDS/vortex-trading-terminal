using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Durable storage for the orders an execution scope owns.</summary>
/// <remarks>
/// The execution scope needs its orders to survive a process restart, and Application must stay free
/// of any specific database, so this port lives here and the adapter in Infrastructure (mirroring how
/// ADR-003 kept persistence out of the inner layers).
/// <para>
/// It stores orders and their fills, nothing else. Positions are deliberately not stored: they are
/// derived from fills (ADR-038), and a stored copy could disagree with the executions that determine
/// it.
/// </para>
/// <para>
/// The contract is a wholesale save of the orders supplied, not a delta. That keeps the adapter free
/// of change-tracking assumptions about the aggregate, at the cost of rewriting rows that did not
/// change. It is not a unit of work and offers no transaction across calls; the caller decides when a
/// consistent point has been reached.
/// </para>
/// </remarks>
public interface IOrderStore
{
    /// <summary>Persists the supplied orders and their fills, inserting or updating as needed.</summary>
    /// <remarks>
    /// Must be safe to call with an order that was already persisted, including one that gained new
    /// fills, and with the same order repeatedly.
    /// </remarks>
    Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default);

    /// <summary>Loads every stored order with its fills, oldest first.</summary>
    Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default);
}
