using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using Vora.Application.Actors.ViewModels;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

/// <summary>
/// Everything on the detail page that is a list, and the profile's watch state
/// and ratings, in one statement that returns one JSON document. MediaDetailsVM.Fields
/// has already loaded the item through the access filter; this is keyed by what
/// that found, so it reads only what the item's type shows.
/// </summary>
internal static class MediaDetailsQuery
{
    private static readonly string Director = ((int)MediaCastRole.Director).ToString(CultureInfo.InvariantCulture);

    // Parameters:
    // - @id: the item. @profileId: the viewer, or null for none.
    // - @inherit: where its genres, studios and directors come from, first that
    //   has any. An episode has few of its own and a season none, so both fall
    //   back to the season and then the show rather than show a blank block.
    // - @castFrom: likewise for the cast. A season shows its show's; an episode
    //   its guest cast, else the show's.
    // - @seasonsOf: the show whose seasons to list. @episodesOf: the season
    //   whose episodes to list. @heldFor: the show whose episodes on disk to
    //   take off its Upcoming list. Each null when the page has none.
    public static readonly string Sql = $$"""
        WITH sources AS (
            SELECT id, rank FROM unnest(@inherit) WITH ORDINALITY AS o(id, rank)
        ),
        genres AS (
            SELECT i.rank, g."Name" AS name
            FROM sources i
            JOIN "MediaItemGenres" mg ON mg."MediaItemsId" = i.id
            JOIN "Genres" g ON g."Id" = mg."GenresId"
        ),
        studios AS (
            SELECT i.rank, c."Name" AS name
            FROM sources i
            JOIN "MediaItemCompanies" mc ON mc."MediaItemsId" = i.id
            JOIN "Companies" c ON c."Id" = mc."ProductionCompaniesId"
        ),
        directors AS (
            SELECT i.rank, cm."Order" AS ord, cm."ActorId" AS actor, coalesce(a."Name", 'Unknown') AS name
            FROM sources i
            JOIN "MediaCastMembers" cm ON cm."MediaItemId" = i.id AND cm."Roles" & {{Director}} <> 0
            LEFT JOIN "Actors" a ON a."Id" = cm."ActorId"
        ),
        cast_members AS (
            SELECT o.rank, cm."Order" AS ord, cm."ActorId" AS actor, a."TmdbId" AS tmdb, a."Name" AS name,
                   cm."CharacterName" AS role_name, cm."Roles" AS roles, a."ProfileImageUrl" AS picture
            FROM unnest(@castFrom) WITH ORDINALITY AS o(id, rank)
            JOIN "MediaCastMembers" cm ON cm."MediaItemId" = o.id
            LEFT JOIN "Actors" a ON a."Id" = cm."ActorId"
        )
        SELECT json_build_object(
            'collectionIds', coalesce((
                SELECT json_agg(ci."CollectionId" ORDER BY ci."CollectionId")
                FROM "CollectionItems" ci WHERE ci."MediaItemId" = @id), '[]'),
            'genres', coalesce((
                SELECT json_agg(name ORDER BY name) FROM genres
                WHERE rank = (SELECT min(rank) FROM genres)), '[]'),
            'studios', coalesce((
                SELECT json_agg(name ORDER BY name) FROM studios
                WHERE rank = (SELECT min(rank) FROM studios)), '[]'),
            'directors', coalesce((
                SELECT json_agg(name ORDER BY ord, actor) FROM directors
                WHERE rank = (SELECT min(rank) FROM directors)), '[]'),
            'cast', coalesce((
                SELECT json_agg(json_build_object(
                    'actorId', actor, 'tmdbId', coalesce(tmdb, 0), 'name', coalesce(name, 'Unknown Actor'),
                    'characterName', role_name, 'roles', roles, 'profileImageUrl', picture) ORDER BY ord, actor)
                FROM cast_members WHERE rank = (SELECT min(rank) FROM cast_members)), '[]'),
            'videos', coalesce((
                SELECT json_agg(json_build_object(
                    'videoKey', v."VideoKey", 'name', v."Name", 'site', v."Site", 'type', v."Type", 'isOfficial', v."IsOfficial")
                    ORDER BY v."Type" = 'Trailer' DESC, v."Id")
                FROM "MediaVideos" v WHERE v."MediaItemId" = @id), '[]'),
            'extras', coalesce((
                SELECT json_agg(json_build_object(
                    'id', e."Id", 'title', e."Title", 'extraType', e."ExtraType",
                    'container', (SELECT p."Container" FROM "MediaParts" p WHERE p."MediaExtraId" = e."Id" ORDER BY p."PartNumber", p."Id" LIMIT 1))
                    ORDER BY e."ExtraType", e."Title", e."Id")
                FROM "MediaExtras" e WHERE e."MediaItemId" = @id), '[]'),
            'markers', coalesce((
                SELECT json_agg(json_build_object(
                    'type', mk."Type", 'startSeconds', date_part('epoch', mk."Start"),
                    'endSeconds', date_part('epoch', mk."End"), 'order', mk."Order")
                    ORDER BY mk."Start", mk."Id")
                FROM "MediaItemMarkers" mk WHERE mk."MediaItemId" = @id), '[]'),
            'mediaParts', coalesce((
                SELECT json_agg(json_build_object(
                    'id', p."Id", 'resolution', p."Resolution", 'edition', p."Edition", 'versionName', p."VersionName",
                    'container', p."Container", 'durationSeconds', date_part('epoch', p."Duration"),
                    'fileSizeBytes', p."FileSizeBytes", 'bitrateKbps', CAST(p."OverallBitrate" / 1000 AS integer),
                    'filePath', p."FilePath",
                    'videoTracks', coalesce((
                        SELECT json_agg(json_build_object(
                            'id', v."Id", 'codec', v."Codec", 'profile', v."Profile", 'hdrType', v."HdrType",
                            'bitDepth', v."BitDepth", 'bitrateKbps', CAST(v."Bitrate" / 1000 AS integer),
                            'width', v."Width", 'height', v."Height", 'isDefault', v."IsDefault")
                            ORDER BY v."StreamIndex", v."Id")
                        FROM "MediaVideoTracks" v WHERE v."MediaPartId" = p."Id"), '[]'),
                    'audioTracks', coalesce((
                        SELECT json_agg(json_build_object(
                            'id', a."Id", 'codec', a."Codec", 'language', a."Language", 'channels', a."Channels",
                            'title', a."Title", 'isDefault', a."IsDefault",
                            'embeddedPosition', (
                                SELECT count(*) FROM "MediaAudioTracks" o
                                WHERE o."MediaPartId" = p."Id" AND o."StreamIndex" < a."StreamIndex"))
                            ORDER BY a."StreamIndex", a."Id")
                        FROM "MediaAudioTracks" a WHERE a."MediaPartId" = p."Id"), '[]'),
                    'subtitleTracks', coalesce((
                        SELECT json_agg(json_build_object(
                            'id', s."Id", 'codec', s."Codec", 'language', s."Language", 'title', s."Title",
                            'isForced', s."IsForced", 'isHearingImpaired', s."IsHearingImpaired", 'isDefault', s."IsDefault",
                            'isExternal', s."ExternalFilePath" IS NOT NULL, 'isDownloaded', s."IsDownloaded",
                            'embeddedPosition', CASE WHEN s."ExternalFilePath" IS NULL THEN (
                                SELECT count(*) FROM "MediaSubtitleTracks" o
                                WHERE o."MediaPartId" = p."Id" AND o."ExternalFilePath" IS NULL AND o."StreamIndex" < s."StreamIndex") END)
                            ORDER BY s."ExternalFilePath" IS NOT NULL, s."StreamIndex", s."Id")
                        FROM "MediaSubtitleTracks" s WHERE s."MediaPartId" = p."Id"), '[]'))
                    ORDER BY p."Id")
                FROM "MediaParts" p WHERE p."MediaItemId" = @id), '[]'),
            'seasons', coalesce((
                SELECT json_agg(json_build_object(
                    'id', s."Id", 'seasonNumber', s."SeasonNumber", 'title', s."Title", 'posterUrl', s."PosterUrl",
                    'episodeCount', ep.span, 'serverAdminRating', s."ServerAdminRating",
                    'unplayedItemCount', CASE WHEN @profileId IS NOT NULL AND ep.total > 0 THEN ep.total - ep.played END,
                    'isPlayed', ep.total > 0 AND ep.total = ep.played,
                    'myRating', (SELECT r."Rating" FROM "UserMediaRatings" r WHERE r."ProfileId" = @profileId AND r."MediaItemId" = s."Id" LIMIT 1))
                    ORDER BY s."SeasonNumber", s."Id")
                FROM "MediaItems" s
                CROSS JOIN LATERAL (
                    SELECT count(*)::int AS total,
                           count(*) FILTER (WHERE EXISTS (
                               SELECT 1 FROM "UserMediaStates" u
                               WHERE u."ProfileId" = @profileId AND u."MediaItemId" = e."Id" AND u."IsPlayed"))::int AS played,
                           coalesce(sum((coalesce(e."EndEpisodeNumber", e."EpisodeNumber") - e."EpisodeNumber") + 1), 0)::int AS span
                    FROM "MediaItems" e
                    WHERE e."MediaType" = 'Episode' AND e."SeasonId" = s."Id" AND e."MissingSince" IS NULL) ep
                WHERE s."MediaType" = 'Season' AND s."TvShowId" = @seasonsOf AND s."MissingSince" IS NULL), '[]'),
            'showEpisodes', (
                SELECT json_build_object(
                    'total', count(*),
                    'played', count(*) FILTER (WHERE EXISTS (
                        SELECT 1 FROM "UserMediaStates" u
                        WHERE u."ProfileId" = @profileId AND u."MediaItemId" = e."Id" AND u."IsPlayed")))
                FROM "MediaItems" e
                JOIN "MediaItems" s ON s."Id" = e."SeasonId" AND s."MediaType" = 'Season'
                WHERE e."MediaType" = 'Episode' AND e."MissingSince" IS NULL AND s."TvShowId" = @seasonsOf),
            'episodes', coalesce((
                SELECT json_agg(json_build_object(
                    'id', e."Id", 'episodeNumber', e."EpisodeNumber", 'endEpisodeNumber', e."EndEpisodeNumber",
                    'title', e."Title", 'overview', e."Overview", 'posterUrl', e."PosterUrl", 'releaseDate', e."ReleaseDate",
                    'durationMinutes', CAST(date_part('epoch', an."Duration") / 60.0 AS integer),
                    'serverAdminRating', e."ServerAdminRating",
                    'isPlayed', coalesce(u."IsPlayed", false), 'resumePositionSeconds', u."ResumePositionSeconds",
                    'myRating', (SELECT r."Rating" FROM "UserMediaRatings" r WHERE r."ProfileId" = @profileId AND r."MediaItemId" = e."Id" LIMIT 1))
                    ORDER BY e."EpisodeNumber", e."Id")
                FROM "MediaItems" e
                LEFT JOIN "MediaItemAnalysis" an ON an."MediaItemId" = e."Id"
                LEFT JOIN "UserMediaStates" u ON u."ProfileId" = @profileId AND u."MediaItemId" = e."Id"
                WHERE e."MediaType" = 'Episode' AND e."SeasonId" = @episodesOf AND e."MissingSince" IS NULL), '[]'),
            'myRating', (
                SELECT r."Rating" FROM "UserMediaRatings" r WHERE r."ProfileId" = @profileId AND r."MediaItemId" = @id LIMIT 1),
            'state', (
                SELECT json_build_object('isPlayed', u."IsPlayed", 'resumePositionSeconds', u."ResumePositionSeconds")
                FROM "UserMediaStates" u WHERE u."ProfileId" = @profileId AND u."MediaItemId" = @id LIMIT 1),
            'heldEpisodes', coalesce((
                SELECT json_agg(json_build_object(
                    'seasonNumber', s."SeasonNumber", 'episodeNumber', e."EpisodeNumber", 'endEpisodeNumber', e."EndEpisodeNumber"))
                FROM "MediaItems" e
                JOIN "MediaItems" s ON s."Id" = e."SeasonId" AND s."MediaType" = 'Season'
                WHERE e."MediaType" = 'Episode' AND e."MissingSince" IS NULL AND s."TvShowId" = @heldFor), '[]')
        )::text AS "Value"
        """;

    public static NpgsqlParameter[] Parameters(MediaDetailsVM item, Guid? profileId)
    {
        var showId = item.Type == "TvShow" ? item.Id : item.TvShowId;
        Guid?[] inherit = item.Type switch
        {
            "Episode" => [item.Id, item.SeasonId, showId],
            "Season" => [item.Id, showId],
            _ => [item.Id]
        };
        Guid?[] castFrom = item.Type switch
        {
            "Season" => [showId],
            "Episode" => [item.Id, showId],
            _ => [item.Id]
        };
        var hasUpcoming = !string.IsNullOrWhiteSpace(item.UpcomingEpisodesJson) && item.UpcomingEpisodesJson != "[]";

        return
        [
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = item.Id },
            Uuid("profileId", profileId),
            Uuids("inherit", inherit),
            Uuids("castFrom", castFrom),
            Uuid("seasonsOf", item.Type == "TvShow" ? item.Id : null),
            Uuid("episodesOf", item.Type == "Season" ? item.Id : null),
            Uuid("heldFor", hasUpcoming ? showId : null)
        ];
    }

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value.HasValue ? value.Value : DBNull.Value };

    private static NpgsqlParameter Uuids(string name, Guid?[] ids) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.OfType<Guid>().ToArray() };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Fills the item's lists and, given a profile, its watch state, the way the
    // page showed them before they were one statement. Returns the show's
    // episodes on disk for its Upcoming list.
    public static IReadOnlyCollection<HeldEpisode> Fill(MediaDetailsVM item, string json, bool hasProfile)
    {
        var lists = JsonSerializer.Deserialize<Lists>(json, Json)
            ?? throw new InvalidOperationException("The detail lists query returned nothing.");

        item.CollectionIds = lists.CollectionIds;
        item.Genres = lists.Genres;
        item.Studios = lists.Studios;
        item.Directors = lists.Directors;
        item.Cast = lists.Cast.Select(c => new CastMemberVM
        {
            ActorId = c.ActorId,
            TmdbId = c.TmdbId,
            Name = c.Name,
            CharacterName = c.CharacterName,
            Roles = (MediaCastRole)c.Roles,
            ProfileImageUrl = c.ProfileImageUrl
        }).ToList();
        item.Videos = lists.Videos;
        item.Extras = lists.Extras;
        item.Markers = lists.Markers;
        item.MediaParts = lists.MediaParts;
        item.Seasons = lists.Seasons;
        item.Episodes = lists.Episodes;

        if (hasProfile) FillWatchState(item, lists);
        return lists.HeldEpisodes;
    }

    private static void FillWatchState(MediaDetailsVM item, Lists lists)
    {
        item.MyRating = lists.MyRating;
        switch (item.Type)
        {
            case "Movie" or "Episode" when lists.State != null:
                item.IsPlayed = lists.State.IsPlayed;
                item.ResumePositionSeconds = lists.State.ResumePositionSeconds;
                break;
            case "TvShow" when lists.ShowEpisodes is { Total: > 0 } show:
                item.UnplayedItemCount = show.Total - show.Played;
                item.IsPlayed = show.Played == show.Total;
                break;
            case "Season":
                item.UnplayedItemCount = item.Episodes.Count(e => !e.IsPlayed);
                item.IsPlayed = item.Episodes.Count > 0 && item.UnplayedItemCount == 0;
                break;
        }
    }

    private sealed record Lists(
        List<Guid> CollectionIds,
        List<string> Genres,
        List<string> Studios,
        List<string> Directors,
        List<CastRow> Cast,
        List<MediaVideoVM> Videos,
        List<MediaExtraVM> Extras,
        List<MediaMarkerVM> Markers,
        List<MediaDetailsPartVM> MediaParts,
        List<SeasonVM> Seasons,
        EpisodeTally? ShowEpisodes,
        List<EpisodeVM> Episodes,
        decimal? MyRating,
        StateRow? State,
        List<HeldEpisode> HeldEpisodes);

    private sealed record CastRow(Guid ActorId, int TmdbId, string Name, string? CharacterName, int Roles, string? ProfileImageUrl);

    private sealed record EpisodeTally(int Total, int Played);

    private sealed record StateRow(bool IsPlayed, double? ResumePositionSeconds);
}
