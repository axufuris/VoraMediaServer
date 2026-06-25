using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaItemAndStreamSessionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaItems_LibraryId",
                table: "MediaItems");

            migrationBuilder.CreateIndex(
                name: "IX_StreamSessions_LastPingAt",
                table: "StreamSessions",
                column: "LastPingAt",
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_LibraryId_MediaType",
                table: "MediaItems",
                columns: new[] { "LibraryId", "MediaType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StreamSessions_LastPingAt",
                table: "StreamSessions");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_LibraryId_MediaType",
                table: "MediaItems");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_LibraryId",
                table: "MediaItems",
                column: "LibraryId");
        }
    }
}
