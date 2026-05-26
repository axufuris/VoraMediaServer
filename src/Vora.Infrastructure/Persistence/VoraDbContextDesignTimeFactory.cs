using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Vora.Infrastructure.Persistence;

public class VoraDbContextDesignTimeFactory : IDesignTimeDbContextFactory<VoraDbContext>
{
    private const string ApiUserSecretsId = "3910658d-a893-4098-a273-605357cbb9e6";
    private const string DefaultConnectionString = "Host=localhost;Port=5434;Database=vora;Username=postgres;Password=postgres";

    public VoraDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();
        var options = new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        return new VoraDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        var builder = new ConfigurationBuilder();

        var apiAppSettings = LocateApiAppSettings();
        if (apiAppSettings != null)
        {
            builder.AddJsonFile(apiAppSettings, optional: true, reloadOnChange: false);
            var apiDir = Path.GetDirectoryName(apiAppSettings);
            if (apiDir != null)
            {
                var devSettings = Path.Combine(apiDir, "appsettings.Development.json");
                if (File.Exists(devSettings))
                {
                    builder.AddJsonFile(devSettings, optional: true, reloadOnChange: false);
                }
            }
        }

        builder.AddUserSecrets(ApiUserSecretsId, reloadOnChange: false);
        builder.AddEnvironmentVariables();

        var configuration = builder.Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        return string.IsNullOrWhiteSpace(connectionString) ? DefaultConnectionString : connectionString;
    }

    private static string? LocateApiAppSettings()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Vora.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            var sibling = Path.Combine(current.FullName, "..", "Vora.Api", "appsettings.json");
            var siblingFull = Path.GetFullPath(sibling);
            if (File.Exists(siblingFull))
            {
                return siblingFull;
            }
            current = current.Parent;
        }
        return null;
    }
}
