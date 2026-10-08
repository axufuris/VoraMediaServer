using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Vora.Application.Maintenance;

namespace Vora.Infrastructure.Persistence.Repositories;

public sealed class StorageReferenceRepository : IStorageReferenceRepository
{
    private const string NameCharacters = "[A-Za-z0-9_.%/+=~-]+";
    private const int ColumnScanTimeoutSeconds = 300;

    private readonly VoraDbContext _context;

    public StorageReferenceRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<string, HashSet<string>>> FindReferencedNamesAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken = default)
    {
        var found = prefixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (found.Count == 0) return found;

        var connection = _context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancellationToken);

        try
        {
            var pattern = $"({string.Join("|", found.Keys.Select(RegexLiteral))})({NameCharacters})";
            var likes = found.Keys.Select(p => $"%{LikeLiteral(p)}%").ToArray();

            foreach (var (schema, table, column) in await ListTextColumnsAsync(connection, cancellationToken))
            {
                var value = $"t.{Identifier(column)}::text";
                await using var command = connection.CreateCommand();
                command.CommandTimeout = ColumnScanTimeoutSeconds;
                command.CommandText =
                    $"SELECT DISTINCT m[1], m[2] FROM {Identifier(schema)}.{Identifier(table)} AS t, " +
                    $"LATERAL regexp_matches({value}, @pattern, 'g') AS m WHERE {value} LIKE ANY (@likes)";
                command.Parameters.Add(new NpgsqlParameter("pattern", NpgsqlDbType.Text) { Value = pattern });
                command.Parameters.Add(new NpgsqlParameter("likes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = likes });

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (found.TryGetValue(reader.GetString(0), out var names)) names.Add(reader.GetString(1));
                }
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return found;
    }

    public async Task<HashSet<Guid>> GetMediaItemIdsAsync(CancellationToken cancellationToken = default) =>
        (await _context.MediaItems.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<HashSet<Guid>> GetMediaPartIdsAsync(CancellationToken cancellationToken = default) =>
        (await _context.MediaParts.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<SubtitleFileReferences> GetSubtitleFileReferencesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.MediaSubtitleTracks
            .AsNoTracking()
            .Select(t => new { t.Id, t.ExternalFilePath })
            .ToListAsync(cancellationToken);

        return new SubtitleFileReferences(
            rows.Select(r => r.Id).ToHashSet(),
            rows.Where(r => r.ExternalFilePath != null).Select(r => r.ExternalFilePath ?? string.Empty).ToList());
    }

    public Task<List<string>> GetOriginalPosterUrlsAsync(CancellationToken cancellationToken = default) =>
        _context.MediaItems
            .AsNoTracking()
            .Where(m => m.OriginalPosterUrl != null && m.OriginalPosterUrl != string.Empty)
            .Select(m => m.OriginalPosterUrl ?? string.Empty)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<List<string>> GetRecordingFilePathsAsync(CancellationToken cancellationToken = default) =>
        _context.IptvRecordingSessions
            .AsNoTracking()
            .Where(s => s.OutputFilePath != null && s.OutputFilePath != string.Empty)
            .Select(s => s.OutputFilePath ?? string.Empty)
            .ToListAsync(cancellationToken);

    private static async Task<List<(string Schema, string Table, string Column)>> ListTextColumnsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.table_schema, c.table_name, c.column_name " +
            "FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name " +
            "WHERE t.table_type = 'BASE TABLE' " +
            "AND c.table_schema = current_schema() " +
            "AND (c.data_type IN ('text', 'character varying', 'character', 'json', 'jsonb') " +
            "OR (c.data_type = 'ARRAY' AND c.udt_name IN ('_text', '_varchar', '_bpchar')))";

        var columns = new List<(string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return columns;
    }

    private static string Identifier(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    internal static string RegexLiteral(string text)
    {
        var builder = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if ("\\.^$|?*+()[]{}".Contains(c)) builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    internal static string LikeLiteral(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
