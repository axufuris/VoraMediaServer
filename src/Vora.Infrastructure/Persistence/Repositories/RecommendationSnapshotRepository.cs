using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Recommendations;
using Vora.Domain.Entities.Users;
using Vora.Plugins.Dtos;

namespace Vora.Infrastructure.Persistence.Repositories;

public class RecommendationSnapshotRepository(VoraDbContext context) : IRecommendationSnapshotRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Row(string ProviderId, string Rows, DateTime GeneratedAt, DateTime? LastPlayedAt, DateTime? LastRatedAt);

    public async Task<StoredRecommendations> GetAsync(Guid profileId, Guid? libraryId, IReadOnlyCollection<string> providerIds, bool withLastActivity)
    {
        var scope = RecommendationSnapshot.ScopeOf(libraryId);
        var ids = providerIds.ToList();
        var stored = context.RecommendationSnapshots
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.LibraryScope == scope && ids.Contains(s.ProviderId));

        // The two maxima are uncorrelated subqueries, which Postgres works out
        // once for the statement, each through the (ProfileId, MediaItemId)
        // index: about 3 ms for a profile with 13,000 watch states. An index on
        // (ProfileId, LastPlayedAt) made it 0.1 ms, but every progress report
        // writes LastPlayedAt and would then update that index as well.
        var rows = withLastActivity
            ? await stored
                .Select(s => new Row(
                    s.ProviderId,
                    s.Rows,
                    s.GeneratedAt,
                    context.UserMediaStates.Where(u => u.ProfileId == profileId).Max(u => (DateTime?)u.LastPlayedAt),
                    context.UserMediaRatings.Where(r => r.ProfileId == profileId).Max(r => (DateTime?)r.RatedAt)))
                .ToListAsync()
            : await stored
                .Select(s => new Row(s.ProviderId, s.Rows, s.GeneratedAt, null, null))
                .ToListAsync();

        var providers = new List<StoredProviderRows>();
        foreach (var row in rows)
        {
            // Rows that can't be read are treated as never built, and are built
            // again.
            var lists = Parse(row.Rows);
            if (lists != null) providers.Add(new StoredProviderRows(row.ProviderId, lists, row.GeneratedAt));
        }

        var lastActivity = rows
            .SelectMany(r => new[] { r.LastPlayedAt, r.LastRatedAt })
            .Max();
        return new StoredRecommendations(providers, lastActivity);
    }

    public async Task SaveAsync(Guid profileId, Guid? libraryId, string providerId, IReadOnlyList<RecommendationListDto> rows, DateTime generatedAt)
    {
        var scope = RecommendationSnapshot.ScopeOf(libraryId);
        var json = JsonSerializer.Serialize(rows, Json);

        // One statement, so a page building the rows and the worker rebuilding
        // them at once can't trip over the key. The newer build wins, and a
        // profile deleted meanwhile gets nothing.
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "RecommendationSnapshots" ("ProfileId", "LibraryScope", "ProviderId", "Rows", "GeneratedAt")
            SELECT {profileId}, {scope}, {providerId}, CAST({json} AS jsonb), {generatedAt}
            WHERE EXISTS (SELECT 1 FROM "UserProfiles" WHERE "Id" = {profileId})
            ON CONFLICT ("ProfileId", "LibraryScope", "ProviderId") DO UPDATE
            SET "Rows" = EXCLUDED."Rows", "GeneratedAt" = EXCLUDED."GeneratedAt"
            WHERE "RecommendationSnapshots"."GeneratedAt" <= EXCLUDED."GeneratedAt"
            """);
    }

    public Task DeleteAsync(Guid profileId, Guid? libraryId, string providerId)
    {
        var scope = RecommendationSnapshot.ScopeOf(libraryId);
        return context.RecommendationSnapshots
            .Where(s => s.ProfileId == profileId && s.LibraryScope == scope && s.ProviderId == providerId)
            .ExecuteDeleteAsync();
    }

    private static List<RecommendationListDto>? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RecommendationListDto>>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
