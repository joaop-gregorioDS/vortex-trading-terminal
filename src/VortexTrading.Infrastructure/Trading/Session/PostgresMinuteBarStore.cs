using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Infrastructure.Persistence;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>Stores the catalog's one-minute bars in PostgreSQL.</summary>
public sealed class PostgresMinuteBarStore(IDbContextFactory<VortexTradingDbContext> contexts) : IMinuteBarStore
{
    /// <summary>How far back a restart reloads. Twelve cash sessions fit inside this window.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(16);

    private const int ChunkSize = 2_000;

    public async Task SaveAsync(IReadOnlyList<StoredMinuteBars> batches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(contexts);

        var rows = new List<Row>(batches.Sum(batch => batch.Bars.Count));
        foreach (var batch in batches)
        {
            foreach (var bar in batch.Bars)
            {
                if (bar.Open <= 0m || bar.Close <= 0m)
                {
                    continue;
                }

                rows.Add(new Row(
                    batch.InstrumentId.Value,
                    bar.OpenTime.Value,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume < 0m ? 0m : bar.Volume));
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            throw new InvalidOperationException("The minute-bar store requires a PostgreSQL connection.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for (var offset = 0; offset < rows.Count; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, rows.Count - offset);
            await using var command = new NpgsqlCommand(UpsertSql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = rows.Skip(offset).Take(count).Select(row => row.InstrumentId).ToArray()
            });
            command.Parameters.Add(new NpgsqlParameter("times", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = rows.Skip(offset).Take(count).Select(row => row.OpenTime).ToArray()
            });
            command.Parameters.Add(Numeric("opens", rows, offset, count, row => row.Open));
            command.Parameters.Add(Numeric("highs", rows, offset, count, row => row.High));
            command.Parameters.Add(Numeric("lows", rows, offset, count, row => row.Low));
            command.Parameters.Add(Numeric("closes", rows, offset, count, row => row.Close));
            command.Parameters.Add(Numeric("volumes", rows, offset, count, row => row.Volume));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredMinuteBars>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        var since = DateTimeOffset.UtcNow - Retention;
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await context.MinuteBars
            .AsNoTracking()
            .Where(bar => bar.OpenTime >= since)
            .OrderBy(bar => bar.InstrumentId)
            .ThenBy(bar => bar.OpenTime)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.InstrumentId)
            .Select(group => new StoredMinuteBars(
                new InstrumentId(group.Key),
                group.Select(row => new SessionBar(
                    new UtcInstant(row.OpenTime),
                    row.Open,
                    row.High,
                    row.Low,
                    row.Close,
                    row.Volume)).ToArray()))
            .ToArray();
    }

    private static NpgsqlParameter Numeric(string name, List<Row> rows, int offset, int count, Func<Row, decimal> value) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Numeric)
        {
            Value = rows.Skip(offset).Take(count).Select(value).ToArray()
        };

    private const string UpsertSql =
        """
        INSERT INTO trading.minute_bars ("InstrumentId", "OpenTime", "Open", "High", "Low", "Close", "Volume")
        SELECT * FROM unnest(@ids, @times, @opens, @highs, @lows, @closes, @volumes)
        ON CONFLICT ("InstrumentId", "OpenTime") DO UPDATE SET
            "Open" = EXCLUDED."Open",
            "High" = EXCLUDED."High",
            "Low" = EXCLUDED."Low",
            "Close" = EXCLUDED."Close",
            "Volume" = EXCLUDED."Volume"
        """;

    private readonly record struct Row(
        Guid InstrumentId,
        DateTimeOffset OpenTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);
}
