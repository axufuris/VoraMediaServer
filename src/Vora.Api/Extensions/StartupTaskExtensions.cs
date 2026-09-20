using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Plugins;
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
        await SeedPluginSettingsFromEnvironmentAsync(app);
        await InitializeFolderWatchersAsync(app);
        await PreloadIptvEpgCacheAsync(app);
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

    private static async Task InitializeFolderWatchersAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var libraryRepo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
            var folderWatcher = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();
            var libraries = await libraryRepo.GetAllLibrariesAsync();

            foreach (var library in libraries)
            {
                if (!library.EnableRealTimeWatching || library.FolderPaths == null || library.FolderPaths.Count == 0)
                {
                    continue;
                }

                folderWatcher.StartWatching(library.Id, library.FolderPaths);
                logger.LogInformation("Auto-started folder watching for library: {LibraryName}", library.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing folder watchers.");
        }
    }

    private static async Task PreloadIptvEpgCacheAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var epgService = scope.ServiceProvider.GetRequiredService<IIptvEpgService>();
        await epgService.LoadCacheIntoMemoryAsync();
    }
}
