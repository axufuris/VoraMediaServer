using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Plugins;
using Vora.Application.Settings;
using Vora.Application.Watchers;
using Vora.Infrastructure.FileSystem;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Extensions;

public static class StartupTaskExtensions
{
    public static async Task RunVoraStartupTasksAsync(this WebApplication app)
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        await MigrateDatabaseAsync(app);
        await EnsureServerSettingsAsync(app);
        await CheckStorageFoldersAsync(app);
        await SeedPluginSettingsFromEnvironmentAsync(app);
        await RefreshVoraPosterAddressesAsync(app);
        await PreloadIptvEpgCacheAsync(app);
    }

    private static async Task RefreshVoraPosterAddressesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            await scope.ServiceProvider.GetRequiredService<Vora.Application.Artwork.IVoraPosterAddressRefresher>().RefreshAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not move collections to the current version of their Vora poster; they keep the address they have.");
        }
    }

    private static async Task EnsureServerSettingsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>().GetSettingsForUpdateAsync();
    }

    private static async Task CheckStorageFoldersAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var paths = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StoragePathsOptions>>().Value;
            var settings = await scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>().GetSettingsAsync();
            var folders = StorageRoots.All(paths, settings.DvrStoragePath)
                .Append((nameof(StoragePathsOptions.Subtitles), Vora.Application.Subtitles.SubtitleStorePath.Resolve(paths)))
                .ToList();

            logger.LogInformation("Storage folders: {Folders}", string.Join(", ", folders.Select(f => $"{f.Item1}={f.Item2}")));

            var notifications = scope.ServiceProvider.GetRequiredService<Vora.Application.Notifications.IAdminNotificationManager>();
            foreach (var problem in StorageFolderCheck.FindUnwritable(folders))
            {
                logger.LogError(
                    "Vora can't write to {Folder} (StoragePaths__{Setting}): {Reason} Fix the folder's permissions, or set StoragePaths__{Setting} to a folder Vora can write to, then restart.",
                    problem.Folder, problem.Setting, problem.Reason, problem.Setting);

                await notifications.RaiseAsync(
                    Vora.Domain.Enums.AdminNotificationSeverity.Error,
                    "A storage folder can't be written to",
                    $"Vora can't write to \"{problem.Folder}\" (StoragePaths__{problem.Setting}). Features that save files there will fail. Fix the folder's permissions, or point the setting at a folder Vora can write to, then restart.",
                    $"{{\"storageFolder\":\"{problem.Setting}\"}}",
                    deduplicateByContext: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not check the storage folders.");
        }
    }

    private static async Task SeedPluginSettingsFromEnvironmentAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IPluginSettingsEnvSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed plugin settings from environment.");
        }
    }

    private static async Task MigrateDatabaseAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();

            // EF works out what to apply by reading __EFMigrationsHistory. On a
            // database that has never been migrated that table does not exist,
            // so the read fails and EF logs it at Error before going on to
            // create the schema. Nothing is wrong — but an Error on a first run
            // reads like something is, so say what it means before it appears.
            if (await IsNewDatabaseAsync(db))
            {
                logger.LogInformation(
                    "This database has no tables yet, so it is being set up from scratch. The failed __EFMigrationsHistory lookup logged next is how EF detects that, and is expected here.");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation("Database schema is up to date.");
                return;
            }

            logger.LogInformation("Applying {Count} pending database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed. Aborting startup.");
            throw;
        }
    }

    // Asks whether the database has any tables at all, which is answerable
    // without touching the migrations-history table that may not exist yet.
    // Only used to decide whether to explain the Error that follows, so a
    // failure here must stay silent rather than become noise of its own.
    private static async Task<bool> IsNewDatabaseAsync(VoraDbContext db)
    {
        try
        {
            return !await db.Database.GetService<IRelationalDatabaseCreator>().HasTablesAsync();
        }
        catch
        {
            return false;
        }
    }

    // Folder watching is started by StartupWatcherService, the hosted service, and
    // used to ALSO be started here. StartWatching reconciles the library against
    // disk, so booting ran that sweep twice and could queue two library scans for
    // the same library — which is what put two "Scan Library" tasks in the queue
    // on every restart.

    private static async Task PreloadIptvEpgCacheAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var epgService = scope.ServiceProvider.GetRequiredService<IIptvEpgService>();
        await epgService.LoadCacheIntoMemoryAsync();
    }
}
