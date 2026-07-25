using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDedupeSourceScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceBluRay",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceDvd",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceHdtv",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceRemux",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceWebDl",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoreSourceWebRip",
                table: "MediaDedupeSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScoreSourceBluRay",
                table: "MediaDedupeSettings");

            migrationBuilder.DropColumn(
                name: "ScoreSourceDvd",
                table: "MediaDedupeSettings");

            migrationBuilder.DropColumn(
                name: "ScoreSourceHdtv",
                table: "MediaDedupeSettings");

            migrationBuilder.DropColumn(
                name: "ScoreSourceRemux",
                table: "MediaDedupeSettings");

            migrationBuilder.DropColumn(
                name: "ScoreSourceWebDl",
                table: "MediaDedupeSettings");

            migrationBuilder.DropColumn(
                name: "ScoreSourceWebRip",
                table: "MediaDedupeSettings");
        }
    }
}
