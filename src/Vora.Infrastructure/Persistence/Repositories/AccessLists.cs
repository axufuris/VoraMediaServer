using Microsoft.EntityFrameworkCore;

namespace Vora.Infrastructure.Persistence.Repositories;

internal static class AccessLists
{
    public static async Task RemoveLibraryAsync(VoraDbContext context, Guid libraryId, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlAsync($"""
            UPDATE "Users" SET "AllowedLibraryIds" = (
                SELECT COALESCE('[' || string_agg(to_jsonb(e.id)::text, ',' ORDER BY e.ord) || ']', '[]')
                FROM jsonb_array_elements_text("AllowedLibraryIds"::jsonb) WITH ORDINALITY AS e(id, ord)
                WHERE lower(e.id) <> {libraryId}::text)
            WHERE strpos(lower("AllowedLibraryIds"), {libraryId}::text) > 0
            """, cancellationToken);

        await context.Database.ExecuteSqlAsync($"""
            UPDATE "UserProfiles" SET "AllowedLibraryIds" = (
                SELECT COALESCE('[' || string_agg(to_jsonb(e.id)::text, ',' ORDER BY e.ord) || ']', '[]')
                FROM jsonb_array_elements_text("AllowedLibraryIds"::jsonb) WITH ORDINALITY AS e(id, ord)
                WHERE lower(e.id) <> {libraryId}::text)
            WHERE strpos(lower("AllowedLibraryIds"), {libraryId}::text) > 0
            """, cancellationToken);
    }

    public static async Task RemoveIptvPlaylistAsync(VoraDbContext context, Guid playlistId, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlAsync($"""
            UPDATE "Users" SET "AllowedIptvPlaylistIds" = (
                SELECT COALESCE('[' || string_agg(to_jsonb(e.id)::text, ',' ORDER BY e.ord) || ']', '[]')
                FROM jsonb_array_elements_text("AllowedIptvPlaylistIds"::jsonb) WITH ORDINALITY AS e(id, ord)
                WHERE lower(e.id) <> {playlistId}::text)
            WHERE strpos(lower("AllowedIptvPlaylistIds"), {playlistId}::text) > 0
            """, cancellationToken);

        await context.Database.ExecuteSqlAsync($"""
            UPDATE "UserProfiles" SET "AllowedIptvPlaylistIds" = (
                SELECT COALESCE('[' || string_agg(to_jsonb(e.id)::text, ',' ORDER BY e.ord) || ']', '[]')
                FROM jsonb_array_elements_text("AllowedIptvPlaylistIds"::jsonb) WITH ORDINALITY AS e(id, ord)
                WHERE lower(e.id) <> {playlistId}::text)
            WHERE strpos(lower("AllowedIptvPlaylistIds"), {playlistId}::text) > 0
            """, cancellationToken);
    }
}
