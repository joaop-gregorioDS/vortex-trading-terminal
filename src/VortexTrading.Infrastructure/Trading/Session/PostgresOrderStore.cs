using Microsoft.EntityFrameworkCore;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Persistence;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Persists execution-scope orders in PostgreSQL.</summary>
/// <remarks>
/// Each call takes a short-lived context from the factory rather than sharing one. The execution scope
/// is a singleton whose writes are serialized, and a DbContext is neither thread-safe nor meant to live
/// for the process, so a per-call context is the shape that matches both facts.
/// <para>
/// The aggregate is written as a wholesale save: an order and every fill it carries are inserted or
/// updated, and nothing is deleted. Fills are immutable once created (ADR-032), so an update to an
/// existing fill is not expected; the adapter conforms to the domain rather than assuming otherwise.
/// </para>
/// <para>
/// This is deliberately not a hidden reconciliation layer. An order that was live when the process
/// died is restored with the lifecycle the database recorded, and no attempt is made to invent the
/// facts that were in flight (ADR-039).
/// </para>
/// </remarks>
public sealed class PostgresOrderStore : IOrderStore
{
    private readonly IDbContextFactory<VortexTradingDbContext> _contexts;

    public PostgresOrderStore(IDbContextFactory<VortexTradingDbContext>? contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _contexts = contexts;
    }

    public async Task SaveAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (orders.Count == 0)
        {
            return;
        }

        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var order in orders)
        {
            var existing = await context.Orders
                .Include(stored => stored.Fills)
                .FirstOrDefaultAsync(stored => stored.Id == order.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                // A new aggregate is inserted whole: EF already handles an order and its fills in one
                // graph.
                context.Orders.Add(order);
                continue;
            }

            // An existing aggregate is updated field by field. Assigning the incoming instance to the
            // tracked entry is not an option: the domain exposes no setters, so EF cannot copy the
            // values, and detaching the tracked graph to attach a new one would risk exactly the
            // duplicate-key failure this branch exists to avoid.
            context.Entry(existing).Property(stored => stored.Status).CurrentValue = order.Status;
            context.Entry(existing).Property(stored => stored.LastUpdatedAt).CurrentValue = order.LastUpdatedAt;
            context.Entry(existing).Property(stored => stored.RejectionReason).CurrentValue = order.RejectionReason;

            var known = existing.Fills.Select(fill => fill.ExecutionId).ToHashSet();
            foreach (var fill in order.Fills.Where(fill => !known.Contains(fill.ExecutionId)))
            {
                context.OrderFills.Add(fill);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Order>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.Orders
            .Include(order => order.Fills)
            .OrderBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
