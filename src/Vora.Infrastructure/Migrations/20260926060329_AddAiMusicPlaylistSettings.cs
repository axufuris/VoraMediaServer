using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMusicPlaylistSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiMusicPlaylistsEnabled",
                table: "UserProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AiPlaylistRequestsPerDay",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAiMusicPlaylists",
                table: "ServerSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAiPlaylistRequests",
                table: "ServerSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "AiPlaylistRequestsPerDay", "EnableAiMusicPlaylists", "EnableAiPlaylistRequests" },
                values: new object[] { 10, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiMusicPlaylistsEnabled",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "AiPlaylistRequestsPerDay",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EnableAiMusicPlaylists",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EnableAiPlaylistRequests",
                table: "ServerSettings");
        }
    }
}
