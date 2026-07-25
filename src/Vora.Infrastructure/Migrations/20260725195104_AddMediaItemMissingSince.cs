using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaItemMissingSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "MediaItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_MissingSince",
                table: "MediaItems",
                column: "MissingSince",
                filter: "\"MissingSince\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaItems_MissingSince",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MediaItems");
        }
    }
}
