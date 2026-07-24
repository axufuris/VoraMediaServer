using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DefaultAdminThemeDark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                column: "AdminThemeId",
                value: "vora-dark");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                column: "AdminThemeId",
                value: "vora-default");
        }
    }
}
