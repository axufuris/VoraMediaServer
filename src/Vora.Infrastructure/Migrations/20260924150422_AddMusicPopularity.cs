using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicPopularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GlobalListeners",
                table: "MediaItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GlobalPlays",
                table: "MediaItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GlobalListeners",
                table: "Artists",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GlobalPlays",
                table: "Artists",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PopularityRefreshedAt",
                table: "Artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GlobalPlays",
                table: "Albums",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalListeners",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "GlobalPlays",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "GlobalListeners",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "GlobalPlays",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "PopularityRefreshedAt",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "GlobalPlays",
                table: "Albums");
        }
    }
}
