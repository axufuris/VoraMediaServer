using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangesSinceInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""Energy"" integer;
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""GoodFor"" text[];
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""IsInstrumental"" boolean;
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""Moods"" text[];
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""ProfiledAt"" timestamp with time zone;
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""Themes"" text[];
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""FullyRefreshedAt"" timestamp with time zone;
ALTER TABLE ""MediaItemEmbeddings"" ADD COLUMN IF NOT EXISTS ""Model"" character varying(64);
ALTER TABLE ""MediaItemEmbeddings"" ADD COLUMN IF NOT EXISTS ""SourceHash"" character varying(64);
ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""AiPlaylistMatchWindow"" double precision NOT NULL DEFAULT 0.04;
ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""HardwareAccelerationType"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""StreamSessions"" ADD COLUMN IF NOT EXISTS ""IpAddress"" character varying(45);
ALTER TABLE ""StreamSessions"" ADD COLUMN IF NOT EXISTS ""Location"" character varying(128);
ALTER TABLE ""MediaLibraries"" ADD COLUMN IF NOT EXISTS ""LocalMetadataMode"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""MediaLibraries"" ADD COLUMN IF NOT EXISTS ""PreferLocalArtwork"" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""PreferredAudioLanguage"" character varying(16);
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""PreferredSubtitleLanguage"" character varying(16);
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""SubtitleMode"" integer;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""PreferHearingImpairedSubtitles"" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE ""MediaVideoTracks"" ADD COLUMN IF NOT EXISTS ""Width"" integer;
ALTER TABLE ""MediaVideoTracks"" ADD COLUMN IF NOT EXISTS ""Height"" integer;
ALTER TABLE ""MediaVideoTracks"" ADD COLUMN IF NOT EXISTS ""FrameRate"" double precision;
ALTER TABLE ""StreamSessions"" ADD COLUMN IF NOT EXISTS ""OutputWidth"" integer;
ALTER TABLE ""StreamSessions"" ADD COLUMN IF NOT EXISTS ""OutputHeight"" integer;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""LibrarySort"" integer;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""LibrarySortDescending"" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""PreserveHdrInTranscodes"" boolean NOT NULL DEFAULT TRUE;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""AutoPlayNext"" boolean NOT NULL DEFAULT TRUE;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""UpNextOrder"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""EpisodeLayout"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""ClientDevices"" ADD COLUMN IF NOT EXISTS ""RendersImageSubtitles"" boolean;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoCodec"" character varying(32);
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoProfile"" character varying(32);
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoLevel"" integer;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoHeight"" integer;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoFrameRate"" double precision;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoInterlaced"" boolean;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""VideoFormatSeenAt"" timestamp with time zone;");

            migrationBuilder.Sql(@"
UPDATE ""UserMediaStates"" SET ""ResumePositionSeconds"" = 0
WHERE ""ResumePositionSeconds"" > 0 AND ""ResumePositionSeconds"" < 30;");

            migrationBuilder.Sql(@"
UPDATE ""MediaVideoTracks"" AS video SET ""Bitrate"" = NULL
FROM ""MediaParts"" AS part
WHERE video.""MediaPartId"" = part.""Id"" AND part.""OverallBitrate"" > 0 AND video.""Bitrate"" > part.""OverallBitrate"";");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'ServerSettings' AND column_name = 'SetupGuideStatus') THEN
        ALTER TABLE ""ServerSettings"" ADD COLUMN ""SetupGuideContent"" integer NOT NULL DEFAULT 3;
        ALTER TABLE ""ServerSettings"" ADD COLUMN ""SetupGuideStatus"" integer NOT NULL DEFAULT 0;
        ALTER TABLE ""ServerSettings"" ADD COLUMN ""SetupGuideStep"" character varying(64);
        UPDATE ""ServerSettings"" SET ""SetupGuideStatus"" = 2 WHERE EXISTS (SELECT 1 FROM ""Users"" WHERE ""IsAdmin"");
    END IF;
END $$;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION pg_temp.vora_try_jsonb(value text) RETURNS jsonb AS $$
BEGIN
    RETURN value::jsonb;
EXCEPTION WHEN others THEN
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
DO $smartlists$
BEGIN
    IF to_regclass('""ProfileChannelFavorites""') IS NULL THEN
        ALTER TABLE ""SmartLists"" ADD ""DefaultKey"" character varying(64);

        ALTER TABLE ""SmartLists"" ADD ""Source"" integer NOT NULL DEFAULT 0;

        CREATE TABLE ""ProfileChannelFavorites"" (
            ""ProfileId"" uuid NOT NULL,
            ""PlaylistId"" uuid NOT NULL,
            ""ExternalChannelId"" character varying(256) NOT NULL,
            ""AddedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_ProfileChannelFavorites"" PRIMARY KEY (""ProfileId"", ""PlaylistId"", ""ExternalChannelId""),
            CONSTRAINT ""FK_ProfileChannelFavorites_IptvPlaylists_PlaylistId"" FOREIGN KEY (""PlaylistId"") REFERENCES ""IptvPlaylists"" (""Id"") ON DELETE CASCADE,
            CONSTRAINT ""FK_ProfileChannelFavorites_UserProfiles_ProfileId"" FOREIGN KEY (""ProfileId"") REFERENCES ""UserProfiles"" (""Id"") ON DELETE CASCADE
        );

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-added-movies-shows', ""Source"" = 0
        WHERE ""Id"" = '17ddede2-2de0-42b8-9b33-32708b4d29b8';

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-released-episodes', ""Source"" = 0
        WHERE ""Id"" = '58424b85-b6da-4a9c-8204-e364f1319508';

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-released-movies-episodes', ""Source"" = 0
        WHERE ""Id"" = '73c33c2c-1fe6-4885-875e-481a1dac5462';

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-added-movies', ""Source"" = 0
        WHERE ""Id"" = 'c88d6c8a-57ea-4b24-a7be-3f2638a38aca';

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-added-shows', ""Source"" = 0
        WHERE ""Id"" = 'dfc420d4-421c-4e14-aec4-a5bedefd2f2e';

        UPDATE ""SmartLists"" SET ""DefaultKey"" = 'recently-released-movies', ""Source"" = 0
        WHERE ""Id"" = 'ebbefd92-4232-4cae-9c5d-2134943b8bf8';

        INSERT INTO ""SmartLists"" (""Id"", ""ActiveEndDay"", ""ActiveEndMonth"", ""ActiveStartDay"", ""ActiveStartMonth"", ""CollectionId"", ""DefaultKey"", ""DisplayOrder"", ""FilterRulesJson"", ""LibraryId"", ""MaxItems"", ""ShowOnHomepage"", ""ShowToFriends"", ""SortBy"", ""Source"", ""Title"")
        VALUES ('1cca6bf0-87a6-4186-82a0-1d1efce4e6b8', NULL, NULL, NULL, NULL, NULL, 'recent-recordings', 7, '{}', NULL, 20, TRUE, TRUE, 0, 5, 'Recent Recordings');
        INSERT INTO ""SmartLists"" (""Id"", ""ActiveEndDay"", ""ActiveEndMonth"", ""ActiveStartDay"", ""ActiveStartMonth"", ""CollectionId"", ""DefaultKey"", ""DisplayOrder"", ""FilterRulesJson"", ""LibraryId"", ""MaxItems"", ""ShowOnHomepage"", ""ShowToFriends"", ""SortBy"", ""Source"", ""Title"")
        VALUES ('2133070b-8810-4b2e-9514-619a682b04b1', NULL, NULL, NULL, NULL, NULL, 'new-podcast-episodes', 9, '{""unwatchedOnly"":true,""days"":14}', NULL, 20, TRUE, TRUE, 0, 3, 'New Podcast Episodes');
        INSERT INTO ""SmartLists"" (""Id"", ""ActiveEndDay"", ""ActiveEndMonth"", ""ActiveStartDay"", ""ActiveStartMonth"", ""CollectionId"", ""DefaultKey"", ""DisplayOrder"", ""FilterRulesJson"", ""LibraryId"", ""MaxItems"", ""ShowOnHomepage"", ""ShowToFriends"", ""SortBy"", ""Source"", ""Title"")
        VALUES ('666c043f-f0f7-47f8-810b-8b0d5afcaeb9', NULL, NULL, NULL, NULL, NULL, 'recently-added-music', 8, '{}', NULL, 20, TRUE, TRUE, 0, 4, 'Recently Added Music');
        INSERT INTO ""SmartLists"" (""Id"", ""ActiveEndDay"", ""ActiveEndMonth"", ""ActiveStartDay"", ""ActiveStartMonth"", ""CollectionId"", ""DefaultKey"", ""DisplayOrder"", ""FilterRulesJson"", ""LibraryId"", ""MaxItems"", ""ShowOnHomepage"", ""ShowToFriends"", ""SortBy"", ""Source"", ""Title"")
        VALUES ('80d62ff0-9b1a-4381-a03a-2595af4b1d9d', NULL, NULL, NULL, NULL, NULL, 'favorite-channels', 6, '{}', NULL, 30, TRUE, TRUE, 6, 1, 'Favorite Channels');
        INSERT INTO ""SmartLists"" (""Id"", ""ActiveEndDay"", ""ActiveEndMonth"", ""ActiveStartDay"", ""ActiveStartMonth"", ""CollectionId"", ""DefaultKey"", ""DisplayOrder"", ""FilterRulesJson"", ""LibraryId"", ""MaxItems"", ""ShowOnHomepage"", ""ShowToFriends"", ""SortBy"", ""Source"", ""Title"")
        VALUES ('ee067d88-cd48-4382-8b05-1f75b39020eb', NULL, NULL, NULL, NULL, NULL, 'favorite-stations', 10, '{}', NULL, 30, TRUE, TRUE, 6, 2, 'Favorite Radio Stations');

        CREATE UNIQUE INDEX ""IX_SmartLists_DefaultKey"" ON ""SmartLists"" (""DefaultKey"");

        CREATE INDEX ""IX_ProfileChannelFavorites_PlaylistId"" ON ""ProfileChannelFavorites"" (""PlaylistId"");

        UPDATE ""SmartLists"" AS s
        SET ""DisplayOrder"" = s.""DisplayOrder"" - 6 + o.""Next""
        FROM (SELECT COALESCE(MAX(""DisplayOrder""), -1) + 1 AS ""Next"" FROM ""SmartLists"" WHERE ""Source"" = 0) AS o
        WHERE s.""Source"" <> 0 AND s.""DefaultKey"" IS NOT NULL;

        INSERT INTO ""ProfileChannelFavorites"" (""ProfileId"", ""PlaylistId"", ""ExternalChannelId"", ""AddedAt"")
        SELECT DISTINCT s.""ProfileId"", c.""PlaylistId"", c.""ExternalChannelId"", now()
        FROM ""ProfileDeviceSettings"" s
        CROSS JOIN LATERAL (SELECT pg_temp.vora_try_jsonb(s.""IptvPrefsJson"") AS prefs) p
        CROSS JOIN LATERAL jsonb_array_elements_text(
            CASE WHEN jsonb_typeof(p.prefs) = 'object' AND jsonb_typeof(p.prefs -> 'favoriteChannels') = 'array'
                 THEN p.prefs -> 'favoriteChannels' ELSE '[]'::jsonb END) AS f(external_id)
        JOIN ""IptvChannels"" c ON lower(c.""ExternalChannelId"") = lower(f.external_id) AND c.""Kind"" = 0
        ON CONFLICT DO NOTHING;

        INSERT INTO ""ProfileChannelFavorites"" (""ProfileId"", ""PlaylistId"", ""ExternalChannelId"", ""AddedAt"")
        SELECT DISTINCT r.""ProfileId"", c.""PlaylistId"", c.""ExternalChannelId"", now()
        FROM (
            SELECT up.""Id"" AS ""ProfileId"", up.""RadioPrefsJson"" AS ""Prefs"" FROM ""UserProfiles"" up
            UNION ALL
            SELECT ds.""ProfileId"", ds.""RadioPrefsJson"" FROM ""ProfileDeviceSettings"" ds
        ) r
        CROSS JOIN LATERAL (SELECT pg_temp.vora_try_jsonb(r.""Prefs"") AS prefs) p
        CROSS JOIN LATERAL jsonb_array_elements_text(
            CASE WHEN jsonb_typeof(p.prefs) = 'object' AND jsonb_typeof(p.prefs -> 'favoriteIds') = 'array'
                 THEN p.prefs -> 'favoriteIds' ELSE '[]'::jsonb END) AS f(channel_id)
        JOIN ""IptvChannels"" c ON c.""Id""::text = lower(f.channel_id) AND c.""Kind"" = 1
        ON CONFLICT DO NOTHING;
    END IF;
END $smartlists$;");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""PendingTasks"" (
    ""Id"" uuid NOT NULL,
    ""Sequence"" bigint NOT NULL,
    ""Kind"" character varying(128) NOT NULL,
    ""ArgumentsJson"" text NOT NULL,
    ""Name"" character varying(512) NOT NULL,
    ""QueuedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_PendingTasks"" PRIMARY KEY (""Id"")
);

CREATE INDEX IF NOT EXISTS ""IX_PendingTasks_Sequence"" ON ""PendingTasks"" (""Sequence"");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ProfileTitlePlaybackChoices"" (
    ""ProfileId"" uuid NOT NULL,
    ""DeviceId"" character varying(128) NOT NULL,
    ""TitleId"" uuid NOT NULL,
    ""RemembersVersion"" boolean NOT NULL,
    ""Edition"" character varying(128),
    ""VersionHeight"" integer NOT NULL,
    ""VideoCodec"" character varying(32),
    ""HdrType"" character varying(32),
    ""RemembersAudio"" boolean NOT NULL,
    ""AudioLanguage"" character varying(16),
    ""AudioIsCommentary"" boolean NOT NULL,
    ""AudioCodec"" character varying(32),
    ""AudioChannels"" integer NOT NULL,
    ""Subtitle"" integer NOT NULL,
    ""SubtitleLanguage"" character varying(16),
    ""SubtitleIsForced"" boolean NOT NULL,
    ""SubtitleIsHearingImpaired"" boolean NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_ProfileTitlePlaybackChoices"" PRIMARY KEY (""ProfileId"", ""DeviceId"", ""TitleId""),
    CONSTRAINT ""FK_ProfileTitlePlaybackChoices_MediaItems_TitleId"" FOREIGN KEY (""TitleId"") REFERENCES ""MediaItems"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_ProfileTitlePlaybackChoices_UserProfiles_ProfileId"" FOREIGN KEY (""ProfileId"") REFERENCES ""UserProfiles"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ""IX_ProfileTitlePlaybackChoices_TitleId"" ON ""ProfileTitlePlaybackChoices"" (""TitleId"");");

            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Albums_ArtistId_Title"";
CREATE INDEX ""IX_Albums_ArtistId_Title"" ON ""Albums"" (""ArtistId"", ""Title"");");

            migrationBuilder.Sql(@"
ALTER TABLE ""MediaItems"" ADD COLUMN IF NOT EXISTS ""Keywords"" text[];
ALTER TABLE ""Collections"" ADD COLUMN IF NOT EXISTS ""VisibleEveryYear"" boolean NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS ""CollectionThemes"" (
    ""CollectionId"" uuid NOT NULL,
    ""Theme"" character varying(128) NOT NULL,
    ""Profile"" character varying(4000) NOT NULL,
    ""Keywords"" text[] NOT NULL,
    ""Embedding"" real[],
    ""EmbeddingModel"" character varying(64),
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_CollectionThemes"" PRIMARY KEY (""CollectionId""),
    CONSTRAINT ""FK_CollectionThemes_Collections_CollectionId"" FOREIGN KEY (""CollectionId"") REFERENCES ""Collections"" (""Id"") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ""CollectionThemeVerdicts"" (
    ""CollectionId"" uuid NOT NULL,
    ""MediaItemId"" uuid NOT NULL,
    ""Belongs"" boolean NOT NULL,
    ""JudgedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_CollectionThemeVerdicts"" PRIMARY KEY (""CollectionId"", ""MediaItemId""),
    CONSTRAINT ""FK_CollectionThemeVerdicts_Collections_CollectionId"" FOREIGN KEY (""CollectionId"") REFERENCES ""Collections"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_CollectionThemeVerdicts_MediaItems_MediaItemId"" FOREIGN KEY (""MediaItemId"") REFERENCES ""MediaItems"" (""Id"") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ""IX_CollectionThemeVerdicts_MediaItemId"" ON ""CollectionThemeVerdicts"" (""MediaItemId"");");

            migrationBuilder.Sql(@"
ALTER TABLE ""TrackPlayHistory"" ADD COLUMN IF NOT EXISTS ""Source"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""TrackPlayHistory"" ADD COLUMN IF NOT EXISTS ""SourceId"" uuid;");

            // The home rows' sort indexes (docs/database.md). EF can't model
            // an expression index, so each must stay the exact ORDER BY that
            // SmartListRepository.ApplySortOrder produces.
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_MediaItems_PresentByReleaseDate"" ON ""MediaItems"" ((""ReleaseDate"" IS NOT NULL), ""ReleaseDate"") WHERE ""MissingSince"" IS NULL;
CREATE INDEX IF NOT EXISTS ""IX_MediaItems_PresentByDateAdded"" ON ""MediaItems"" ((COALESCE(""LastContentAddedAt"", ""AddedAt""))) WHERE ""MissingSince"" IS NULL;");

            // Search's trigram indexes (docs/database.md). They are only an
            // accelerator: search runs the same ILIKE with or without them.
            // pg_trgm ships with Postgres and is a trusted extension, but a
            // server without it, or a database user who may not install
            // it, must still start. So any error here is a warning and the
            // migration carries on without them. Never take this out of
            // its block, and never declare the extension in the model:
            // EF would then emit a CREATE EXTENSION that can fail.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE INDEX IF NOT EXISTS ""IX_MediaItems_TitleTrigram"" ON ""MediaItems"" USING gin (""Title"" gin_trgm_ops);
    CREATE INDEX IF NOT EXISTS ""IX_Actors_NameTrigram"" ON ""Actors"" USING gin (""Name"" gin_trgm_ops);
    CREATE INDEX IF NOT EXISTS ""IX_Artists_NameTrigram"" ON ""Artists"" USING gin (""Name"" gin_trgm_ops);
    CREATE INDEX IF NOT EXISTS ""IX_Albums_TitleTrigram"" ON ""Albums"" USING gin (""Title"" gin_trgm_ops);
EXCEPTION WHEN OTHERS THEN
    RAISE WARNING 'Vora skipped the search indexes, so search reads every title: %', SQLERRM;
END
$$;");

            // The For You rows each provider last built, per profile, library
            // (Guid.Empty for Home) and provider (docs/for-you.md). It starts
            // empty: each profile's rows are built on its next visit.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""RecommendationSnapshots"" (
    ""ProfileId"" uuid NOT NULL,
    ""LibraryScope"" uuid NOT NULL,
    ""ProviderId"" character varying(128) NOT NULL,
    ""Rows"" jsonb NOT NULL,
    ""GeneratedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_RecommendationSnapshots"" PRIMARY KEY (""ProfileId"", ""LibraryScope"", ""ProviderId""),
    CONSTRAINT ""FK_RecommendationSnapshots_UserProfiles_ProfileId"" FOREIGN KEY (""ProfileId"") REFERENCES ""UserProfiles"" (""Id"") ON DELETE CASCADE
);");

            // The AI rows' titles began "AI: ". They carry isAi instead, and
            // the clients draw a small mark (docs/for-you.md). Stored AI rows
            // lose the prefix and gain the flag. Rows already flagged are
            // left alone, so running it again changes nothing.
            migrationBuilder.Sql(@"
UPDATE ""RecommendationSnapshots"" AS s
SET ""Rows"" = (
    SELECT COALESCE(jsonb_agg(
        CASE WHEN jsonb_typeof(r.elem) = 'object' THEN
            r.elem || jsonb_build_object('isAi', true)
                || CASE WHEN left(r.elem ->> 'title', 4) = 'AI: '
                        THEN jsonb_build_object('title', substr(r.elem ->> 'title', 5))
                        ELSE '{}'::jsonb END
        ELSE r.elem END
        ORDER BY r.ord), '[]'::jsonb)
    FROM jsonb_array_elements(s.""Rows"") WITH ORDINALITY AS r(elem, ord)
)
WHERE s.""ProviderId"" = 'openai_recommendations'
  AND CASE WHEN jsonb_typeof(s.""Rows"") = 'array' THEN EXISTS (
          SELECT 1 FROM jsonb_array_elements(s.""Rows"") AS e(elem)
          WHERE jsonb_typeof(e.elem) = 'object' AND (e.elem -> 'isAi') IS DISTINCT FROM 'true'::jsonb)
      ELSE false END;");

            // Providers that need an account (docs/iptv-and-dvr.md). Every
            // playlist stays the M3U playlist it was, with no credentials and
            // no headers. The provider's guide is on, which only an Xtream
            // Codes account reads.
            migrationBuilder.Sql(@"
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""SourceType"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""ServerUrl"" character varying(1024);
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""Username"" character varying(256);
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""PasswordCiphertext"" text;
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""UserAgent"" character varying(512);
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""Referer"" character varying(1024);
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""UseProviderGuide"" boolean NOT NULL DEFAULT TRUE;
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""AccountStatus"" character varying(32);
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""AccountExpiresAt"" timestamp with time zone;
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""AccountMaxConnections"" integer;
ALTER TABLE ""IptvPlaylists"" ADD COLUMN IF NOT EXISTS ""AccountCheckedAt"" timestamp with time zone;
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""UserAgent"" character varying(512);
ALTER TABLE ""IptvChannels"" ADD COLUMN IF NOT EXISTS ""Referer"" character varying(1024);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Referer",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "AccountCheckedAt",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "AccountMaxConnections",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "AccountExpiresAt",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "UseProviderGuide",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "Referer",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "PasswordCiphertext",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "ServerUrl",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "IptvPlaylists");

            migrationBuilder.DropTable(
                name: "RecommendationSnapshots");

            // The extension stays: something else in the database may use it.
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Albums_TitleTrigram"";
DROP INDEX IF EXISTS ""IX_Artists_NameTrigram"";
DROP INDEX IF EXISTS ""IX_Actors_NameTrigram"";
DROP INDEX IF EXISTS ""IX_MediaItems_TitleTrigram"";");

            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_MediaItems_PresentByDateAdded"";
DROP INDEX IF EXISTS ""IX_MediaItems_PresentByReleaseDate"";");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TrackPlayHistory");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "TrackPlayHistory");

            migrationBuilder.DropTable(
                name: "CollectionThemeVerdicts");

            migrationBuilder.DropTable(
                name: "CollectionThemes");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VisibleEveryYear",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "VideoCodec",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoProfile",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoLevel",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoHeight",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoFrameRate",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoInterlaced",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "VideoFormatSeenAt",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "RendersImageSubtitles",
                table: "ClientDevices");

            migrationBuilder.DropColumn(
                name: "UpNextOrder",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "EpisodeLayout",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "AutoPlayNext",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PreserveHdrInTranscodes",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "LibrarySort",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LibrarySortDescending",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "MediaVideoTracks");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "MediaVideoTracks");

            migrationBuilder.DropColumn(
                name: "FrameRate",
                table: "MediaVideoTracks");

            migrationBuilder.DropColumn(
                name: "OutputWidth",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "OutputHeight",
                table: "StreamSessions");

            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Albums_ArtistId_Title"";
CREATE UNIQUE INDEX ""IX_Albums_ArtistId_Title"" ON ""Albums"" (""ArtistId"", ""Title"");");

            migrationBuilder.DropColumn(
                name: "PreferredAudioLanguage",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PreferredSubtitleLanguage",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SubtitleMode",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PreferHearingImpairedSubtitles",
                table: "UserProfiles");

            migrationBuilder.DropTable(
                name: "ProfileTitlePlaybackChoices");

            migrationBuilder.DropTable(
                name: "PendingTasks");

            migrationBuilder.DropTable(
                name: "ProfileChannelFavorites");

            migrationBuilder.DropIndex(
                name: "IX_SmartLists_DefaultKey",
                table: "SmartLists");

            migrationBuilder.DeleteData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("1cca6bf0-87a6-4186-82a0-1d1efce4e6b8"));

            migrationBuilder.DeleteData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("2133070b-8810-4b2e-9514-619a682b04b1"));

            migrationBuilder.DeleteData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("666c043f-f0f7-47f8-810b-8b0d5afcaeb9"));

            migrationBuilder.DeleteData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("80d62ff0-9b1a-4381-a03a-2595af4b1d9d"));

            migrationBuilder.DeleteData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("ee067d88-cd48-4382-8b05-1f75b39020eb"));

            migrationBuilder.DropColumn(
                name: "DefaultKey",
                table: "SmartLists");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SmartLists");

            migrationBuilder.DropColumn(
                name: "AiPlaylistMatchWindow",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "HardwareAccelerationType",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "LocalMetadataMode",
                table: "MediaLibraries");

            migrationBuilder.DropColumn(
                name: "PreferLocalArtwork",
                table: "MediaLibraries");

            migrationBuilder.DropColumn(
                name: "SetupGuideContent",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SetupGuideStatus",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SetupGuideStep",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "Energy",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "GoodFor",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "IsInstrumental",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Moods",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "ProfiledAt",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Themes",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "FullyRefreshedAt",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "MediaItemEmbeddings");

            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "MediaItemEmbeddings");
        }
    }
}
