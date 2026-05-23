using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailHeight",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailIntervalSeconds",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailJpegQuality",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "VideoThumbnailScheduleTime",
                table: "ServerSettings",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailSpriteColumns",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailWidth",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVideoThumbnailGenerationAt",
                table: "MediaItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailHeight",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailIntervalSeconds",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailSpriteColumns",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailSpriteCount",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VideoThumbnailSpriteVersion",
                table: "MediaItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailWidth",
                table: "MediaItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "VideoThumbnailHeight", "VideoThumbnailIntervalSeconds", "VideoThumbnailJpegQuality", "VideoThumbnailScheduleTime", "VideoThumbnailSpriteColumns", "VideoThumbnailWidth" },
                values: new object[] { 180, 10, 5, new TimeSpan(0, 4, 0, 0, 0), 10, 320 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoThumbnailHeight",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailIntervalSeconds",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailJpegQuality",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailScheduleTime",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteColumns",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailWidth",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "LastVideoThumbnailGenerationAt",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailHeight",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailIntervalSeconds",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteColumns",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteCount",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteVersion",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailWidth",
                table: "MediaItems");
        }
    }
}
