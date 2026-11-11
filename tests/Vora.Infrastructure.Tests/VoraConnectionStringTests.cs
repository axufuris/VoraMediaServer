using Npgsql;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Tests.Postgres;

namespace Vora.Infrastructure.Tests;

// Postgres's JIT compiled the library page's query for 5 s before running it
// for 0.15 s, so Vora's connections turn it off.
public class VoraConnectionStringTests
{
    private static string? Options(string? connectionString) =>
        new NpgsqlConnectionStringBuilder(VoraConnectionString.WithJitOff(connectionString)).Options;

    [Fact]
    public void A_connection_turns_jit_off()
    {
        Options("Host=db;Database=vora;Username=vora;Password=x").Should().Be("-c jit=off");
    }

    [Fact]
    public void Options_already_given_are_kept()
    {
        Options("Host=db;Options=-c search_path=vora").Should().Be("-c search_path=vora -c jit=off");
    }

    [Fact]
    public void A_connection_string_that_sets_jit_itself_is_left_as_it_is()
    {
        Options("Host=db;Options=-c jit=on").Should().Be("-c jit=on");
    }

    [Fact]
    public void No_connection_string_stays_none()
    {
        VoraConnectionString.WithJitOff(null).Should().BeNull();
    }

    [Fact]
    public async Task Postgres_runs_the_connections_queries_without_jit()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var connection = new NpgsqlConnection(VoraConnectionString.WithJitOff(PostgresDatabase.ServerConnection));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var show = new NpgsqlCommand("SHOW jit", connection);

        (await show.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("off");
    }
}
