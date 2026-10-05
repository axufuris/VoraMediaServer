using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vora.Application.Backups;

public static class BackupJson
{
    public const string IdentitiesFileSuffix = "identities.json";

    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonSerializerOptions ManifestOptions { get; } = new()
    {
        WriteIndented = true
    };

    public static int CountRows<T>(string path, T payload)
    {
        if (path.EndsWith(IdentitiesFileSuffix, StringComparison.Ordinal)) return 0;
        return payload is ICollection rows ? rows.Count : 0;
    }
}
