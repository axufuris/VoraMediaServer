using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollectionManualAddAndExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludedMediaIdsJson",
                table: "Collections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyAdded",
                table: "CollectionItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedMediaIdsJson",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ManuallyAdded",
                table: "CollectionItems");
        }
    }
}
