using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupGuide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SetupGuideContent",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "SetupGuideStatus",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SetupGuideStep",
                table: "ServerSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"ServerSettings\" SET \"SetupGuideStatus\" = 2 " +
                "WHERE EXISTS (SELECT 1 FROM \"Users\" WHERE \"IsAdmin\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupGuideContent",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SetupGuideStatus",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SetupGuideStep",
                table: "ServerSettings");
        }
    }
}
