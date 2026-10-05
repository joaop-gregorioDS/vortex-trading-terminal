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
/// The existing keys are read once per call, up front, and every row is then classified from that one
/// snapshot. The previous version asked the database about each order individually and decided insert
/// or update from the answer, which inserted an order twice whenever two saves overlapped: the second
/// one looked before the first had committed and concluded the order was new. Reading the keys once
/// removes that window and makes the call idempotent by construction.
/// </para>
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

        var orderIds = orders.Select(order => order.Id).ToArray();
        var knownOrders = (await context.Orders
                .Where(stored => orderIds.Contains(stored.Id))
                .Select(stored => stored.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var executionIds = orders.SelectMany(order => order.Fills).Select(fill => fill.ExecutionId).ToArray();
        var knownFills = (await context.OrderFills
                .Where(stored => executionIds.Contains(stored.ExecutionId))
                .Select(stored => stored.ExecutionId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        foreach (var order in orders)
        {
            var isKnown = knownOrders.Contains(order.Id);

            if (isKnown)
            {
                // The lifecycle columns are the only ones that can change after creation. Production
                // identity and the quantity are immutable, and assigning the incoming instance to the
                // tracked entry is not possible because the domain exposes no setters.
                var entry = context.Attach(order);
                entry.Property(stored => stored.Status).IsModified = true;
                entry.Property(stored => stored.LastUpdatedAt).IsModified = true;
                entry.Property(stored => stored.RejectionReason).IsModified = true;
            }
            else
            {
                context.Orders.Add(order);
            }

            // Fills are handled the same way whether the order already existed or not: a fill whose
            // execution identity is already stored needs no write, and a new one must be inserted.
            // Skipping this for a known order silently dropped every subsequent partial fill.
            foreach (var fill in order.Fills.Where(fill => !knownFills.Contains(fill.ExecutionId)))
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
