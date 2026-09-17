using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteAccessExternalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteAccessExternalUrl",
                table: "ServerSettings",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "RemoteAccessExternalUrl", "VideoThumbnailHeight", "VideoThumbnailJpegQuality", "VideoThumbnailWidth" },
                values: new object[] { null, 90, 9, 160 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteAccessExternalUrl",
                table: "ServerSettings");

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "VideoThumbnailHeight", "VideoThumbnailJpegQuality", "VideoThumbnailWidth" },
                values: new object[] { 180, 5, 320 });
        }
    }
}
