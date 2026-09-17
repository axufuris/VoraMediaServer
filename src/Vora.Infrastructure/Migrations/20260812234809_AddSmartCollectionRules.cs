using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartCollectionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RulesJson",
                table: "Collections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmartMediaType",
                table: "Collections",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RulesJson",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "SmartMediaType",
                table: "Collections");
        }
    }
}
