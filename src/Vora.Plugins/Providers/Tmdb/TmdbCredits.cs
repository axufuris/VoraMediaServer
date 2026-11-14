using System.Text.Json;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Tmdb;

internal sealed class TmdbCredits
{
    private readonly Dictionary<int, CastMemberResult> _people = new();
    private readonly List<CastMemberResult> _actors = new();
    private readonly List<CastMemberResult> _crew = new();
    private readonly Dictionary<int, int> _crewRank = new();

    public void AddActor(JsonElement member)
    {
        if (!TryReadId(member, out var tmdbId) || _people.ContainsKey(tmdbId)) return;

        var actor = Person(member, tmdbId, CastRole.Actor);
        actor.CharacterName = member.TryGetProperty("character", out var character) && character.ValueKind == JsonValueKind.String ? character.GetString() : null;
        _people[tmdbId] = actor;
        _actors.Add(actor);
    }

    public void AddCrew(JsonElement member, CastRole kept) => AddCredits(member, TmdbCrewJobs.JobsOf(member), kept);

    public void AddCreator(JsonElement member) => AddCredits(member, [TmdbCrewJobs.Creator], CastRole.Creator);

    public List<CastMemberResult> People() =>
        [.. _actors, .. _crew.OrderBy(person => _crewRank[person.TmdbId])];

    private void AddCredits(JsonElement member, IEnumerable<string> jobs, CastRole kept)
    {
        var credited = jobs.Where(job => (TmdbCrewJobs.RoleFor(job) & kept) != CastRole.None).ToList();
        if (credited.Count == 0 || !TryReadId(member, out var tmdbId)) return;

        var roles = credited.Aggregate(CastRole.None, (all, job) => all | TmdbCrewJobs.RoleFor(job));
        var rank = credited.Min(TmdbCrewJobs.RankOf);

        if (_people.TryGetValue(tmdbId, out var known))
        {
            known.Roles |= roles;
            if (_crewRank.TryGetValue(tmdbId, out var current) && rank < current) _crewRank[tmdbId] = rank;
            return;
        }

        var person = Person(member, tmdbId, roles);
        _people[tmdbId] = person;
        _crew.Add(person);
        _crewRank[tmdbId] = rank;
    }

    private static CastMemberResult Person(JsonElement member, int tmdbId, CastRole roles) => new()
    {
        TmdbId = tmdbId,
        Name = member.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "Unknown" : "Unknown",
        ProfileImageUrl = member.TryGetProperty("profile_path", out var path) && path.ValueKind == JsonValueKind.String ? $"https://image.tmdb.org/t/p/w185{path.GetString()}" : null,
        Roles = roles
    };

    private static bool TryReadId(JsonElement member, out int tmdbId)
    {
        tmdbId = 0;
        return member.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out tmdbId);
    }
}
