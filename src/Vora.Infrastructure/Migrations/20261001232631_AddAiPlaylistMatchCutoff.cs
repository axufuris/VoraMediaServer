using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPlaylistMatchCutoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AiPlaylistMatchCutoff",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.55);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiPlaylistMatchCutoff",
                table: "ServerSettings");
        }
    }
}
