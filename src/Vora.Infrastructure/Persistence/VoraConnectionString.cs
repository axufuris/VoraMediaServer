using Npgsql;

namespace Vora.Infrastructure.Persistence;

public static class VoraConnectionString
{
    /// <summary>
    /// Turns Postgres's JIT off for Vora's connections. A stock Postgres compiles
    /// any query its planner prices above <c>jit_above_cost</c> to machine code
    /// first, and Vora's big projections (a library page's tiles, each with its
    /// watched state and rating) cross that line: on QA the movie library's
    /// query took 5.3 s with JIT and 0.15 s without, nearly all of it compiling.
    /// It is set per connection, so it holds on any Postgres without touching
    /// the server's settings. A connection string that sets jit itself wins.
    /// </summary>
    public static string? WithJitOff(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var options = builder.Options ?? string.Empty;
        if (options.Contains("jit=", StringComparison.OrdinalIgnoreCase)) return builder.ConnectionString;

        builder.Options = string.IsNullOrWhiteSpace(options) ? "-c jit=off" : options.Trim() + " -c jit=off";
        return builder.ConnectionString;
    }
}
