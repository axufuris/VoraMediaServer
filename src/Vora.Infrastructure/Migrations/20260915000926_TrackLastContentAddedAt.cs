using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackLastContentAddedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastContentAddedAt",
                table: "MediaItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "MediaItems" AS s
                SET "LastContentAddedAt" = e.latest
                FROM (
                    SELECT "SeasonId", MAX("AddedAt") AS latest
                    FROM "MediaItems"
                    WHERE "MediaType" = 'Episode' AND "MissingSince" IS NULL AND "SeasonId" IS NOT NULL
                    GROUP BY "SeasonId"
                ) AS e
                WHERE s."Id" = e."SeasonId" AND s."MediaType" = 'Season';
                """);

            migrationBuilder.Sql("""
                UPDATE "MediaItems" AS sh
                SET "LastContentAddedAt" = x.latest
                FROM (
                    SELECT "TvShowId", MAX("LastContentAddedAt") AS latest
                    FROM "MediaItems"
                    WHERE "MediaType" = 'Season' AND "LastContentAddedAt" IS NOT NULL
                    GROUP BY "TvShowId"
                ) AS x
                WHERE sh."Id" = x."TvShowId" AND sh."MediaType" = 'TvShow';
                """);

            migrationBuilder.Sql("""
                UPDATE "MediaItems" AS s
                SET "MissingSince" = now()
                WHERE s."MediaType" = 'Season'
                  AND s."MissingSince" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "MediaItems" AS e
                      WHERE e."SeasonId" = s."Id" AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL);
                """);

            migrationBuilder.Sql("""
                UPDATE "MediaItems" AS sh
                SET "MissingSince" = now()
                WHERE sh."MediaType" = 'TvShow'
                  AND sh."MissingSince" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "MediaItems" AS s
                      JOIN "MediaItems" AS e ON e."SeasonId" = s."Id"
                      WHERE s."TvShowId" = sh."Id" AND e."MediaType" = 'Episode' AND e."MissingSince" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastContentAddedAt",
                table: "MediaItems");
        }
    }
}
