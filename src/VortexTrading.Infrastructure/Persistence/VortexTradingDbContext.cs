using Microsoft.EntityFrameworkCore;

namespace VortexTrading.Infrastructure.Persistence;

public sealed class VortexTradingDbContext : DbContext
{
    public VortexTradingDbContext(DbContextOptions<VortexTradingDbContext> options)
        : base(options)
    {
    }
}
