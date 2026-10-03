using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

public sealed class PostgresDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "VORA_TEST_POSTGRES";
    public const string SkipReason = "Set VORA_TEST_POSTGRES to a pgvector Postgres server to run the database tests.";

    private readonly string _name = "vora_test_" + Guid.NewGuid().ToString("N");
    private string _connectionString = string.Empty;

    public static string? ServerConnection => Environment.GetEnvironmentVariable(ConnectionVariable);
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ServerConnection);

    public async ValueTask InitializeAsync()
    {
        if (!IsConfigured) return;

        _connectionString = new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _name }.ConnectionString;
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql(_connectionString, npgsql => npgsql.UseVector())
            .Options);

    public async ValueTask DisposeAsync()
    {
        if (!IsConfigured) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ServerConnection) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
