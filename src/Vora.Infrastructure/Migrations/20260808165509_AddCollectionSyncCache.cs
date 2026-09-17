using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionSyncCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChronologyItemsSignature",
                table: "Collections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentSyncCacheJson",
                table: "Collections",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChronologyItemsSignature",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ContentSyncCacheJson",
                table: "Collections");
        }
    }
}
