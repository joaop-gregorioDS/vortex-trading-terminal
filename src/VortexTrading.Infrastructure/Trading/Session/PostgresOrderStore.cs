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

        // A session-level advisory lock serializes writers across connections. Reading the keys once
        // inside a transaction was not enough: the execution scope adds an order to its in-memory book
        // before this call awaits, so two saves could both decide an order was new and insert it, and
        // the loser died on the primary key. The advisory lock makes the read-decide-write sequence
        // atomic with respect to other writers of this schema, which is the guarantee the previous
        // version only appeared to have.
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // 4242 is an arbitrary but fixed key that belongs to this store alone.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_lock(4242)", cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteAsync(context, orders, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock(4242)", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM trading.order_fills", cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM trading.orders", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteAsync(VortexTradingDbContext context, IReadOnlyCollection<Order> orders,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

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

        // Entities this context already tracks count as present too, so an id repeated within one call
        // cannot collide with itself.
        var trackedOrders = context.ChangeTracker.Entries<Order>()
            .Select(entry => entry.Entity.Id)
            .ToHashSet();
        var trackedFills = context.ChangeTracker.Entries<OrderFill>()
            .Select(entry => entry.Entity.ExecutionId)
            .ToHashSet();

        foreach (var order in orders)
        {
            var entry = context.ChangeTracker.Entries<Order>().FirstOrDefault(candidate => candidate.Entity.Id == order.Id);
            if (entry is null && knownOrders.Contains(order.Id))
            {
                entry = context.Attach(order);
            }

            if (entry is not null)
            {
                // The lifecycle columns are the only ones that can change after creation. Production
                // identity and the quantity are immutable, and assigning the incoming instance to the
                // tracked entry is not possible because the domain exposes no setters.
                entry.Property(nameof(Order.Status)).IsModified = true;
                entry.Property(nameof(Order.LastUpdatedAt)).IsModified = true;
                entry.Property(nameof(Order.RejectionReason)).IsModified = true;
            }
            else
            {
                context.Orders.Add(order);
            }

            // Fills are handled the same way whether the order already existed or not: a fill whose
            // execution identity is already stored needs no write, and a new one must be inserted.
            // Skipping this for a known order silently dropped every subsequent partial fill.
            foreach (var fill in order.Fills.Where(fill =>
                         !knownFills.Contains(fill.ExecutionId) && !trackedFills.Contains(fill.ExecutionId)))
            {
                context.OrderFills.Add(fill);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
