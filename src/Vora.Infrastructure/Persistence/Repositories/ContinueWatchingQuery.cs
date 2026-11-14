using System.Globalization;
using Vora.Application.Media;
using Vora.Application.Streaming;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

/// <summary>
/// The profile's Continue Watching row in one statement: films part way
/// through, and each show's next episode by the rules EpisodeToPlay uses for
/// the Play button (docs/frontend-client-ui.md). Shows are taken newest
/// first and only until the row is full, so a long watch history costs no
/// more than a short one. Parameters: <c>@profileId</c> and <c>@limit</c>.
/// </summary>
internal static class ContinueWatchingQuery
{
    private static readonly string ResumeSeconds = WatchStateTransition.MinimumResumeSeconds.ToString(CultureInfo.InvariantCulture);
    private static readonly string Specials = EpisodeSequence.SpecialsSeason.ToString(CultureInfo.InvariantCulture);
    private static readonly string WhereILeftOff = ((int)UpNextOrder.WhereILeftOff).ToString(CultureInfo.InvariantCulture);

    // Shapes that keep the plan cheap, each measured on a 13,000-state profile:
    // - CTEs read more than once are MATERIALIZED; Postgres can't index them,
    //   so nothing below looks rows up in them one at a time.
    // - The lateral subqueries end in LIMIT 1 or OFFSET 0, or Postgres
    //   inlines them into every use and works each out again per use.
    // - The final played-or-hidden check is a scalar subquery: NOT EXISTS
    //   becomes an anti join, which loses the newest-first order and works
    //   out every show before sorting.
    public static readonly string Sql = $$"""
        WITH st AS MATERIALIZED (
            SELECT "MediaItemId" AS id, "IsPlayed" AS played, "ResumePositionSeconds" AS pos,
                   "LastPlayedAt" AS at, "IsHiddenFromContinueWatching" AS hidden
            FROM "UserMediaStates" WHERE "ProfileId" = @profileId
        ),
        ord AS MATERIALIZED (
            SELECT coalesce((SELECT "UpNextOrder" FROM "UserProfiles" WHERE "Id" = @profileId), 0) AS v
        ),
        seasons AS MATERIALIZED (
            SELECT "Id" AS id, "TvShowId" AS show, "SeasonNumber" AS sn FROM "MediaItems" WHERE "MediaType" = 'Season'
        ),
        -- Episodes still on disk that the profile finished or got far enough into.
        watched AS MATERIALIZED (
            SELECT st.id, st.played, st.at, st.hidden, se.show, se.sn, e."EpisodeNumber" AS en
            FROM st
            JOIN "MediaItems" e ON e."Id" = st.id AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL
            JOIN seasons se ON se.id = e."SeasonId"
            WHERE st.played OR st.pos >= {{ResumeSeconds}}
        ),
        -- The episode each show was left on, with specials and without. Ties in
        -- LastPlayedAt (a season marked watched at once) go to the later episode.
        last_any AS (
            SELECT DISTINCT ON (w.show) w.show, w.id, w.played, w.sn, w.en
            FROM watched w ORDER BY w.show, w.at DESC, w.sn DESC, w.en DESC
        ),
        last_regular AS (
            SELECT DISTINCT ON (w.show) w.show, w.id, w.played, w.sn, w.en
            FROM watched w WHERE w.sn <> {{Specials}} ORDER BY w.show, w.at DESC, w.sn DESC, w.en DESC
        ),
        -- Shows the profile is watching and hasn't hidden, newest first.
        shows AS (
            SELECT g.show, g.last_played,
                   la.id AS la_id, la.played AS la_played, la.sn AS la_sn, la.en AS la_en,
                   lr.id AS lr_id, lr.played AS lr_played, lr.sn AS lr_sn, lr.en AS lr_en
            FROM (
                SELECT w.show, max(w.at) AS last_played FROM watched w WHERE NOT w.hidden GROUP BY w.show
            ) g
            LEFT JOIN last_any la ON la.show = g.show
            LEFT JOIN last_regular lr ON lr.show = g.show
            WHERE NOT EXISTS (SELECT 1 FROM "UserMediaStates" h
                              WHERE h."ProfileId" = @profileId AND h."MediaItemId" = g.show AND h."IsHiddenFromContinueWatching")
            ORDER BY g.last_played DESC
        ),
        show_picks AS (
            SELECT c.show, c.last_played, p.pick
            FROM shows c
            -- Specials count only when a show has no other episodes on disk.
            LEFT JOIN LATERAL (
                SELECT true AS yes FROM "MediaItems" rs
                WHERE rs."TvShowId" = c.show AND rs."MediaType" = 'Season' AND rs."SeasonNumber" <> {{Specials}}
                  AND EXISTS (SELECT 1 FROM "MediaItems" re
                              WHERE re."SeasonId" = rs."Id" AND re."MediaType" = 'Episode' AND re."MissingSince" IS NULL)
                LIMIT 1
            ) rg ON true
            CROSS JOIN LATERAL (SELECT rg.yes IS NOT NULL AS has_regular OFFSET 0) r
            CROSS JOIN LATERAL (
                SELECT CASE WHEN r.has_regular THEN c.lr_id ELSE c.la_id END AS id,
                       CASE WHEN r.has_regular THEN c.lr_played ELSE c.la_played END AS played,
                       CASE WHEN r.has_regular THEN c.lr_sn ELSE c.la_sn END AS sn,
                       CASE WHEN r.has_regular THEN c.lr_en ELSE c.la_en END AS en
                OFFSET 0
            ) l
            CROSS JOIN LATERAL (
                SELECT CASE WHEN (SELECT v FROM ord) = {{WhereILeftOff}} THEN
                    -- Where I left off: the episode left part way, else the one
                    -- after the last finished, else the first not finished.
                    CASE WHEN l.id IS NOT NULL AND NOT l.played THEN l.id
                    ELSE coalesce(
                        CASE WHEN l.played THEN (
                            SELECT e."Id" FROM "MediaItems" se JOIN "MediaItems" e ON e."SeasonId" = se."Id"
                            WHERE se."TvShowId" = c.show AND se."MediaType" = 'Season'
                              AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL
                              AND (NOT r.has_regular OR se."SeasonNumber" <> {{Specials}})
                              AND (se."SeasonNumber", e."EpisodeNumber") > (l.sn, l.en)
                            ORDER BY se."SeasonNumber", e."EpisodeNumber" LIMIT 1) END,
                        (SELECT e."Id" FROM "MediaItems" se JOIN "MediaItems" e ON e."SeasonId" = se."Id"
                         WHERE se."TvShowId" = c.show AND se."MediaType" = 'Season'
                           AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL
                           AND (NOT r.has_regular OR se."SeasonNumber" <> {{Specials}})
                           AND NOT EXISTS (SELECT 1 FROM "UserMediaStates" u
                                           WHERE u."ProfileId" = @profileId AND u."MediaItemId" = e."Id" AND u."IsPlayed")
                         ORDER BY se."SeasonNumber", e."EpisodeNumber" LIMIT 1))
                    END
                ELSE (
                    -- In order: a special part way through, else the first
                    -- episode neither finished nor hidden.
                    SELECT e."Id" FROM "MediaItems" se JOIN "MediaItems" e ON e."SeasonId" = se."Id"
                    LEFT JOIN "UserMediaStates" u ON u."ProfileId" = @profileId AND u."MediaItemId" = e."Id"
                    WHERE se."TvShowId" = c.show AND se."MediaType" = 'Season'
                      AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL
                      AND NOT coalesce(u."IsPlayed" OR u."IsHiddenFromContinueWatching", false)
                      AND ((se."SeasonNumber" = {{Specials}} AND coalesce(u."ResumePositionSeconds", 0) >= {{ResumeSeconds}})
                           OR NOT r.has_regular OR se."SeasonNumber" <> {{Specials}})
                    ORDER BY (se."SeasonNumber" = {{Specials}} AND coalesce(u."ResumePositionSeconds", 0) >= {{ResumeSeconds}}) DESC,
                             se."SeasonNumber", e."EpisodeNumber"
                    LIMIT 1)
                END AS pick
                OFFSET 0
            ) p
            WHERE p.pick IS NOT NULL
              AND (SELECT u."IsPlayed" OR u."IsHiddenFromContinueWatching" FROM "UserMediaStates" u
                   WHERE u."ProfileId" = @profileId AND u."MediaItemId" = p.pick) IS NOT TRUE
            ORDER BY c.last_played DESC
            LIMIT @limit
        ),
        top AS (
            SELECT st.id, st.at AS last_played FROM st
            JOIN "MediaItems" m ON m."Id" = st.id AND m."MediaType" = 'Movie' AND m."MissingSince" IS NULL
            WHERE st.pos >= {{ResumeSeconds}} AND NOT st.played AND NOT st.hidden
            UNION ALL
            SELECT pick, last_played FROM show_picks
            ORDER BY last_played DESC
            LIMIT @limit
        )
        -- An episode's tile shows its show: the show's overview, artwork,
        -- rating and genres, with the episode's own artwork as the fallback.
        SELECT i."Id", i."MediaType" AS "Type", i."Title",
               CASE WHEN show."Id" IS NULL THEN i."SortTitle" END AS "SortTitle",
               coalesce(show."Overview", CASE WHEN show."Id" IS NULL THEN i."Overview" END) AS "Overview",
               coalesce(show."PosterUrl", i."PosterUrl") AS "PosterUrl",
               coalesce(show."BackgroundUrl", i."BackgroundUrl") AS "BackgroundUrl",
               i."ReleaseDate",
               CASE WHEN show."Id" IS NULL THEN i."ContentRating" ELSE show."ContentRating" END AS "ContentRating",
               ARRAY(SELECT g."Name" FROM "MediaItemGenres" mg JOIN "Genres" g ON g."Id" = mg."GenresId"
                     WHERE mg."MediaItemsId" = coalesce(show."Id", i."Id"))::text[] AS "Genres",
               coalesce(u."ResumePositionSeconds", 0) AS "ResumePositionSeconds",
               extract(epoch FROM a."Duration")::double precision AS "DurationSeconds",
               show."Id" AS "TvShowId", season."Id" AS "SeasonId", show."Title" AS "TvShowTitle",
               season."SeasonNumber", i."EpisodeNumber"
        FROM top t
        JOIN "MediaItems" i ON i."Id" = t.id
        LEFT JOIN "MediaItems" season ON season."Id" = i."SeasonId"
        LEFT JOIN "MediaItems" show ON show."Id" = season."TvShowId"
        LEFT JOIN "MediaItemAnalysis" a ON a."MediaItemId" = i."Id"
        LEFT JOIN "UserMediaStates" u ON u."ProfileId" = @profileId AND u."MediaItemId" = i."Id"
        ORDER BY t.last_played DESC
        """;
}
