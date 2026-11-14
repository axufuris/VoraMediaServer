using System.Text.Json;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Tmdb;

internal static class TmdbCrewJobs
{
    public const string Creator = "Creator";

    private static readonly (string Job, CastRole Role)[] Credited =
    [
        ("Director", CastRole.Director),
        ("Co-Director", CastRole.Director),
        ("Series Director", CastRole.Director),
        (Creator, CastRole.Creator),
        ("Screenplay", CastRole.Writer),
        ("Writer", CastRole.Writer),
        ("Teleplay", CastRole.Writer),
        ("Co-Writer", CastRole.Writer),
        ("Scenario Writer", CastRole.Writer),
        ("Series Composition", CastRole.Writer),
        ("Dialogue", CastRole.Writer),
        ("Adaptation", CastRole.Writer),
        ("Story", CastRole.Writer),
        ("Screenstory", CastRole.Writer),
        ("Original Story", CastRole.Writer),
        ("Idea", CastRole.Writer),
        ("Original Concept", CastRole.Writer),
        ("Characters", CastRole.Writer),
        ("Original Series Creator", CastRole.Writer),
        ("Original Film Writer", CastRole.Writer),
        ("Novel", CastRole.Writer),
        ("Book", CastRole.Writer),
        ("Author", CastRole.Writer),
        ("Short Story", CastRole.Writer),
        ("Comic Book", CastRole.Writer),
        ("Graphic Novel", CastRole.Writer),
        ("Theatre Play", CastRole.Writer),
        ("Musical", CastRole.Writer),
        ("Opera", CastRole.Writer),
        ("Producer", CastRole.Producer),
        ("Executive Producer", CastRole.Producer),
        ("Co-Producer", CastRole.Producer),
        ("Co-Executive Producer", CastRole.Producer),
        ("Executive Co-Producer", CastRole.Producer),
        ("Supervising Producer", CastRole.Producer),
        ("Coordinating Producer", CastRole.Producer),
        ("Consulting Producer", CastRole.Producer),
        ("Delegated Producer", CastRole.Producer),
        ("Line Producer", CastRole.Producer),
        ("Associate Producer", CastRole.Producer)
    ];

    private static readonly Dictionary<string, (CastRole Role, int Rank)> ByJob = Credited
        .Select((credit, rank) => (credit.Job, credit.Role, rank))
        .ToDictionary(credit => credit.Job, credit => (credit.Role, credit.rank), StringComparer.OrdinalIgnoreCase);

    public static CastRole RoleFor(string? job) =>
        job != null && ByJob.TryGetValue(job.Trim(), out var credit) ? credit.Role : CastRole.None;

    public static int RankOf(string job) =>
        ByJob.TryGetValue(job.Trim(), out var credit) ? credit.Rank : Credited.Length;

    public static IEnumerable<string> JobsOf(JsonElement member)
    {
        if (member.TryGetProperty("job", out var job) && job.ValueKind == JsonValueKind.String && job.GetString() is { } single)
            yield return single;

        if (!member.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array) yield break;
        foreach (var credit in jobs.EnumerateArray())
        {
            if (credit.ValueKind == JsonValueKind.Object && credit.TryGetProperty("job", out var each)
                && each.ValueKind == JsonValueKind.String && each.GetString() is { } named)
                yield return named;
        }
    }
}
