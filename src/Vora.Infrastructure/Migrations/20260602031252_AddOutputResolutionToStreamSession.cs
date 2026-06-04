using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutputResolutionToStreamSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputHdrType",
                table: "StreamSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputResolution",
                table: "StreamSessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputHdrType",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "OutputResolution",
                table: "StreamSessions");
        }
    }
}
