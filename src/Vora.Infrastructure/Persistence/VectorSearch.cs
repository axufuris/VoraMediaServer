using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Vora.Infrastructure.Persistence;

public static class VectorSearch
{
    private const int DefaultEfSearch = 40;
    private const int MaxEfSearch = 1000;
    private static readonly Version IterativeScanVersion = new(0, 8, 0);
    private static int _iterativeScanSupport = -1;

    public static async Task<T> WideAsync<T>(VoraDbContext context, int candidates, Func<Task<T>> query)
    {
        if (!context.Database.IsNpgsql()) return await query();

        IDbContextTransaction? transaction = context.Database.CurrentTransaction == null
            ? await context.Database.BeginTransactionAsync()
            : null;

        try
        {
            var efSearch = Math.Clamp(candidates, DefaultEfSearch, MaxEfSearch).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Database.ExecuteSqlAsync($"SELECT set_config('hnsw.ef_search', {efSearch}, true)");

            if (await SupportsIterativeScanAsync(context))
            {
                await context.Database.ExecuteSqlAsync($"SELECT set_config('hnsw.iterative_scan', 'strict_order', true)");
            }

            var result = await query();
            if (transaction != null) await transaction.CommitAsync();
            return result;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    private static async Task<bool> SupportsIterativeScanAsync(VoraDbContext context)
    {
        if (_iterativeScanSupport >= 0) return _iterativeScanSupport == 1;

        var versions = await context.Database
            .SqlQuery<string>($"SELECT extversion AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
            .ToListAsync();

        var supported = versions.Count > 0
            && Version.TryParse(versions[0], out var installed)
            && installed >= IterativeScanVersion;

        _iterativeScanSupport = supported ? 1 : 0;
        return supported;
    }
}
