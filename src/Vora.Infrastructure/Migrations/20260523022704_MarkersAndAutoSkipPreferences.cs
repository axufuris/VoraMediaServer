using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MarkersAndAutoSkipPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditsStart",
                table: "MediaItemAnalysis");

            migrationBuilder.DropColumn(
                name: "IntroEnd",
                table: "MediaItemAnalysis");

            migrationBuilder.DropColumn(
                name: "IntroStart",
                table: "MediaItemAnalysis");

            migrationBuilder.AddColumn<bool>(
                name: "AutoSkipCredits",
                table: "UserProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoSkipIntro",
                table: "UserProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinimumCreditsSceneSeconds",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "BlackFrameMinDurationSec",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeIntroClusterMinAgreementPct",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeIntroClusterToleranceSec",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SilenceMinDurationEpisodeSec",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SilenceMinDurationMovieSec",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SilenceThresholdOffsetDb",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MediaItemMarkers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Start = table.Column<TimeSpan>(type: "interval", nullable: false),
                    End = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItemMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItemMarkers_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "BlackFrameMinDurationSec", "EpisodeIntroClusterMinAgreementPct", "EpisodeIntroClusterToleranceSec", "SilenceMinDurationEpisodeSec", "SilenceMinDurationMovieSec", "SilenceThresholdOffsetDb" },
                values: new object[] { 0.5, 70, 5, 1.0, 1.5, -12 });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItemMarkers_MediaItemId_Type_Order",
                table: "MediaItemMarkers",
                columns: new[] { "MediaItemId", "Type", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaItemMarkers");

            migrationBuilder.DropColumn(
                name: "AutoSkipCredits",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "AutoSkipIntro",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "MinimumCreditsSceneSeconds",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "BlackFrameMinDurationSec",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EpisodeIntroClusterMinAgreementPct",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EpisodeIntroClusterToleranceSec",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SilenceMinDurationEpisodeSec",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SilenceMinDurationMovieSec",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SilenceThresholdOffsetDb",
                table: "ServerSettings");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CreditsStart",
                table: "MediaItemAnalysis",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "IntroEnd",
                table: "MediaItemAnalysis",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "IntroStart",
                table: "MediaItemAnalysis",
                type: "interval",
                nullable: true);
        }
    }
}
