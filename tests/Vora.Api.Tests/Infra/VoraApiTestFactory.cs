using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests.Infra;

public sealed class VoraApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dpKeysPath = Path.Combine(Path.GetTempPath(), "vora-test-dp-keys-" + Guid.NewGuid().ToString("N"));

    // Set env vars in the static ctor so they're already in process before
    // Program.Main runs CreateBuilder() — the default config provider reads
    // env vars there, which is BEFORE our ConfigureAppConfiguration would
    // otherwise get a chance to override appsettings.json. Without this,
    // AddVoraAuthenticationAndAuthorization eagerly captures the JWT secret
    // from appsettings.json (the placeholder, which triggers the dev fallback)
    // and our test tokens then fail signature validation.
    static VoraApiTestFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtTestHelpers.Secret);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtTestHelpers.Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtTestHelpers.Audience);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=vora_tests_unused;Username=test;Password=test",
                ["Jwt:SecretKey"] = JwtTestHelpers.Secret,
                ["Jwt:Issuer"] = JwtTestHelpers.Issuer,
                ["Jwt:Audience"] = JwtTestHelpers.Audience,
                ["StoragePaths:DataProtection"] = _dpKeysPath,
                ["StoragePaths:Plugins"] = Path.Combine(Path.GetTempPath(), "vora-test-plugins-" + Guid.NewGuid().ToString("N"))
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the pg-backed pooled DbContext with EF Core InMemory.
            // InMemory doesn't support pooling, so swap to AddDbContext.
            // Strip every descriptor whose ServiceType mentions VoraDbContext —
            // that covers DbContextOptions<VoraDbContext>, the pool, the lease,
            // the DbContext itself, and any factory variants — without needing
            // to reference EF Core's internal IDbContextPool type.
            var toRemove = services
                .Where(d => d.ServiceType.FullName?.Contains(nameof(VoraDbContext)) == true)
                .ToList();
            foreach (var d in toRemove)
            {
                services.Remove(d);
            }

            var dbName = "vora-tests-" + Guid.NewGuid().ToString("N");
            services.AddDbContext<VoraDbContext>(options =>
            {
                options.UseInMemoryDatabase(dbName);
                options.EnableSensitiveDataLogging();
            });

            // Stub the security-stamp validator so tests don't need to seed users to
            // pass the OnTokenValidated check. JwtTestHelpers issues real signed JWTs;
            // this just bypasses the DB-backed stamp check.
            services.RemoveAll<Vora.Application.Auth.IJwtSecurityStampValidator>();
            services.AddScoped<Vora.Application.Auth.IJwtSecurityStampValidator, AlwaysValidStampValidator>();

            // Background workers aren't needed for endpoint/auth tests and can throw
            // during host shutdown on slower CI runners (e.g. touching the InMemory
            // DB after its scope is torn down), which surfaces as a test-class cleanup
            // failure. Strip them so the test host stays hermetic and shuts down cleanly.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dpKeysPath))
        {
            try { Directory.Delete(_dpKeysPath, recursive: true); } catch { /* best-effort */ }
        }
    }
}
