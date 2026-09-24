using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackIsrcAndRatingSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentRatingCheckedAt",
                table: "MediaItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentRatingProvider",
                table: "MediaItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Isrc",
                table: "MediaItems",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentRatingCheckedAt",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "ContentRatingProvider",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "Isrc",
                table: "MediaItems");
        }
    }
}
