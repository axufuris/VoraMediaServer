using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Vora.Application.Backups;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups;

public static class BackupTableSync
{
    public static Task ReplaceAsync<TEntity>(VoraDbContext db, IQueryable<TEntity> scope, IReadOnlyCollection<TEntity> rows, CancellationToken ct)
        where TEntity : class =>
        ReplaceAsync(db, scope, rows, (_, _) => Task.CompletedTask, ct);

    public static async Task ReplaceAsync<TEntity>(
        VoraDbContext db,
        IQueryable<TEntity> scope,
        IReadOnlyCollection<TEntity> rows,
        Func<List<TEntity>, CancellationToken, Task> beforeRemovingAsync,
        CancellationToken ct)
        where TEntity : class
    {
        var key = PrimaryKey<TEntity>(db);

        var incoming = new Dictionary<string, TEntity>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            incoming.TryAdd(KeyOf(key, row), row);
        }

        var existing = await scope.ToListAsync(ct);
        var kept = new Dictionary<string, TEntity>(StringComparer.Ordinal);
        var stale = new List<TEntity>();
        foreach (var entity in existing)
        {
            var entityKey = KeyOf(key, entity);
            if (incoming.ContainsKey(entityKey)) kept[entityKey] = entity;
            else stale.Add(entity);
        }

        if (stale.Count > 0)
        {
            await beforeRemovingAsync(stale, ct);
            db.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }

        foreach (var (rowKey, row) in incoming)
        {
            if (kept.TryGetValue(rowKey, out var current))
            {
                db.Entry(current).CurrentValues.SetValues(row);
            }
            else
            {
                db.Add(row);
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public static List<T> KeepOnePer<T, TKey>(
        IEnumerable<T> rows,
        Func<T, TKey> uniqueKey,
        Func<T, DateTime> recency,
        BackupSkipTally tally,
        BackupRowNoun noun)
        where TKey : notnull
    {
        var list = rows.ToList();
        var unique = list
            .GroupBy(uniqueKey)
            .Select(g => g.OrderByDescending(recency).First())
            .ToList();
        tally.Skip(noun, BackupSkipReason.Duplicate, list.Count - unique.Count);
        return unique;
    }

    private static IReadOnlyList<IProperty> PrimaryKey<TEntity>(VoraDbContext db) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.Properties
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key to restore by.");

    private static string KeyOf<TEntity>(IReadOnlyList<IProperty> key, TEntity entity)
    {
        var parts = key.Select(p =>
        {
            var info = p.PropertyInfo ?? throw new InvalidOperationException($"Key property {p.Name} on {typeof(TEntity).Name} has no CLR property.");
            return info.GetValue(entity) switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value.ToString() ?? string.Empty
            };
        });
        return string.Join('\u001f', parts);
    }
}
