using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.Persistence;

namespace VortexTrading.Infrastructure.Tests.Persistence;

/// <summary>
/// Provides a migrated PostgreSQL database for persistence tests, or skips them when no database is
/// configured. CI has no database and must stay green, so the absence of a connection is treated as
/// "not configured" rather than as a failure; a configured but unreachable or malformed database is
/// a real error and fails the test.
/// </summary>
/// <remarks>
/// The database is the development database described in docs/architecture/postgresql-persistence.md.
/// Tests own their rows and remove them afterwards; they never drop the schema or the database.
/// </remarks>
public sealed class PersistenceTestFixture : IAsyncLifetime
{
    private ServiceProvider? _provider;

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    /// <summary>
    /// Instrument reserved for this fixture instance.
    /// </summary>
    /// <remarks>
    /// Unique per instance so a fixture can never delete rows another fixture is still using, and
    /// cleanup deletes only this instrument's rows. An earlier version deleted every order and fill in
    /// the schema, which silently wiped data written by a running API sharing the same development
    /// database.
    /// </remarks>
    public InstrumentId TestInstrument { get; } = new(Guid.NewGuid());

    public static string? ConnectionString() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
        ?? new ConfigurationBuilder()
            .AddUserSecrets(typeof(PersistenceTestFixture).Assembly)
            .Build()["ConnectionStrings:PostgreSQL"];

    public ValueTask InitializeAsync()
    {
        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            SkipReason = "No PostgreSQL connection string configured (ConnectionStrings:PostgreSQL).";
            return ValueTask.CompletedTask;
        }

        var services = new ServiceCollection();
        services.AddDbContext<VortexTradingDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(18, 0)));
        // The order store takes a factory, so the fixture must expose the same registration the API
        // uses rather than a hand-built stand-in.
        services.AddDbContextFactory<VortexTradingDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(18, 0)));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<VortexTradingDbContext>().Database.Migrate();
        IsAvailable = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        if (_provider is not null)
        {
            // Scoped to this fixture's own instrument, so a running application sharing the database
            // keeps its data.
            await using (var scope = _provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<VortexTradingDbContext>();
                await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM trading.order_fills WHERE \"OrderId\" IN (SELECT \"Id\" FROM trading.orders WHERE \"InstrumentId\" = {0})",
                    [TestInstrument.Value], token);
                await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM trading.orders WHERE \"InstrumentId\" = {0}", [TestInstrument.Value], token);
            }

            await _provider.DisposeAsync();
        }
    }

    /// <summary>Opens a context over the migrated database, or skips the calling test.</summary>
    public PersistenceSession OpenSession()
    {
        if (!IsAvailable)
        {
            Assert.Skip(SkipReason ?? "PostgreSQL is not available.");
        }

        return new PersistenceSession(_provider!.CreateScope());
    }

    /// <summary>Creates a context factory for components that take one, or skips the calling test.</summary>
    public IDbContextFactory<VortexTradingDbContext> CreateContextFactory()
    {
        if (!IsAvailable)
        {
            Assert.Skip(SkipReason ?? "PostgreSQL is not available.");
        }

        return _provider!.GetRequiredService<IDbContextFactory<VortexTradingDbContext>>();
    }

    /// <summary>Keeps the scope alive for as long as the context is used.</summary>
    public sealed class PersistenceSession(IServiceScope scope) : IAsyncDisposable
    {
        public VortexTradingDbContext Context { get; } =
            scope.ServiceProvider.GetRequiredService<VortexTradingDbContext>();

        public ValueTask DisposeAsync()
        {
            scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
