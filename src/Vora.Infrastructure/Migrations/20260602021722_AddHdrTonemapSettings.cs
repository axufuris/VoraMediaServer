using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHdrTonemapSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HdrTonemapQuality",
                table: "ServerSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HdrTranscodeDownscale",
                table: "ServerSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "HdrTonemapQuality", "HdrTranscodeDownscale" },
                values: new object[] { "Auto", "Auto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HdrTonemapQuality",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "HdrTranscodeDownscale",
                table: "ServerSettings");
        }
    }
}
