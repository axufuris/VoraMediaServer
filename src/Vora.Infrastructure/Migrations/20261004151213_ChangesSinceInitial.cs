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
ALTER TABLE ""MediaItemEmbeddings"" ADD COLUMN IF NOT EXISTS ""Model"" character varying(64);
ALTER TABLE ""MediaItemEmbeddings"" ADD COLUMN IF NOT EXISTS ""SourceHash"" character varying(64);
ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""AiPlaylistMatchWindow"" double precision NOT NULL DEFAULT 0.04;");

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

            migrationBuilder.AddColumn<string>(
                name: "DefaultKey",
                table: "SmartLists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "SmartLists",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProfileChannelFavorites",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalChannelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileChannelFavorites", x => new { x.ProfileId, x.PlaylistId, x.ExternalChannelId });
                    table.ForeignKey(
                        name: "FK_ProfileChannelFavorites_IptvPlaylists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "IptvPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileChannelFavorites_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("17ddede2-2de0-42b8-9b33-32708b4d29b8"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-added-movies-shows", 0 });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("58424b85-b6da-4a9c-8204-e364f1319508"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-released-episodes", 0 });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-released-movies-episodes", 0 });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("c88d6c8a-57ea-4b24-a7be-3f2638a38aca"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-added-movies", 0 });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("dfc420d4-421c-4e14-aec4-a5bedefd2f2e"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-added-shows", 0 });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("ebbefd92-4232-4cae-9c5d-2134943b8bf8"),
                columns: new[] { "DefaultKey", "Source" },
                values: new object[] { "recently-released-movies", 0 });

            migrationBuilder.InsertData(
                table: "SmartLists",
                columns: new[] { "Id", "ActiveEndDay", "ActiveEndMonth", "ActiveStartDay", "ActiveStartMonth", "CollectionId", "DefaultKey", "DisplayOrder", "FilterRulesJson", "LibraryId", "MaxItems", "ShowOnHomepage", "ShowToFriends", "SortBy", "Source", "Title" },
                values: new object[,]
                {
                    { new Guid("1cca6bf0-87a6-4186-82a0-1d1efce4e6b8"), null, null, null, null, null, "recent-recordings", 10, "{}", null, 20, true, true, 0, 5, "Recent Recordings" },
                    { new Guid("2133070b-8810-4b2e-9514-619a682b04b1"), null, null, null, null, null, "new-podcast-episodes", 8, "{\"unwatchedOnly\":true,\"days\":14}", null, 20, true, true, 0, 3, "New Podcast Episodes" },
                    { new Guid("666c043f-f0f7-47f8-810b-8b0d5afcaeb9"), null, null, null, null, null, "recently-added-music", 7, "{}", null, 20, true, true, 0, 4, "Recently Added Music" },
                    { new Guid("80d62ff0-9b1a-4381-a03a-2595af4b1d9d"), null, null, null, null, null, "favorite-channels", 6, "{}", null, 30, true, true, 6, 1, "Favorite Channels" },
                    { new Guid("ee067d88-cd48-4382-8b05-1f75b39020eb"), null, null, null, null, null, "favorite-stations", 9, "{}", null, 30, true, true, 6, 2, "Favorite Radio Stations" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartLists_DefaultKey",
                table: "SmartLists",
                column: "DefaultKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChannelFavorites_PlaylistId",
                table: "ProfileChannelFavorites",
                column: "PlaylistId");

            migrationBuilder.Sql(@"
UPDATE ""SmartLists"" AS s
SET ""DisplayOrder"" = s.""DisplayOrder"" - 6 + o.""Next""
FROM (SELECT COALESCE(MAX(""DisplayOrder""), -1) + 1 AS ""Next"" FROM ""SmartLists"" WHERE ""Source"" = 0) AS o
WHERE s.""Source"" <> 0 AND s.""DefaultKey"" IS NOT NULL;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION pg_temp.vora_try_jsonb(value text) RETURNS jsonb AS $$
BEGIN
    RETURN value::jsonb;
EXCEPTION WHEN others THEN
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
INSERT INTO ""ProfileChannelFavorites"" (""ProfileId"", ""PlaylistId"", ""ExternalChannelId"", ""AddedAt"")
SELECT DISTINCT s.""ProfileId"", c.""PlaylistId"", c.""ExternalChannelId"", now()
FROM ""ProfileDeviceSettings"" s
CROSS JOIN LATERAL (SELECT pg_temp.vora_try_jsonb(s.""IptvPrefsJson"") AS prefs) p
CROSS JOIN LATERAL jsonb_array_elements_text(
    CASE WHEN jsonb_typeof(p.prefs) = 'object' AND jsonb_typeof(p.prefs -> 'favoriteChannels') = 'array'
         THEN p.prefs -> 'favoriteChannels' ELSE '[]'::jsonb END) AS f(external_id)
JOIN ""IptvChannels"" c ON lower(c.""ExternalChannelId"") = lower(f.external_id) AND c.""Kind"" = 0
ON CONFLICT DO NOTHING;");

            migrationBuilder.Sql(@"
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
ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "Model",
                table: "MediaItemEmbeddings");

            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "MediaItemEmbeddings");
        }
    }
}
