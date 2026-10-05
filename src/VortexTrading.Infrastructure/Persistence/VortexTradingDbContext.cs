using Microsoft.EntityFrameworkCore;

namespace VortexTrading.Infrastructure.Persistence;

/// <summary>Transactional state owned by the Trading module.</summary>
/// <remarks>
/// Tables live in the <c>trading</c> schema rather than <c>public</c>: per-module data ownership is a
/// decision of ADR-001 and the schema is what makes that ownership visible in the database. This
/// does not create a project per module and does not guarantee compilation isolation.
/// Entity configuration lives in <c>Persistence/Configurations</c>; the context stays free of mapping
/// detail. See ADR-035 for why the domain receives no rehydration support.
/// </remarks>
public sealed class VortexTradingDbContext : DbContext
{
    internal const string TradingSchema = "trading";

    public VortexTradingDbContext(DbContextOptions<VortexTradingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Domain.Trading.Order> Orders => Set<Domain.Trading.Order>();

    public DbSet<Domain.Trading.OrderFill> OrderFills => Set<Domain.Trading.OrderFill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(TradingSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VortexTradingDbContext).Assembly);
    }
}
