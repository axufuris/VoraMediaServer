using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerPartVideoThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ThumbnailSourcePartId",
                table: "MediaParts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailHeight",
                table: "MediaParts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailIntervalSeconds",
                table: "MediaParts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailSpriteColumns",
                table: "MediaParts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailSpriteCount",
                table: "MediaParts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VideoThumbnailSpriteVersion",
                table: "MediaParts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoThumbnailWidth",
                table: "MediaParts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailSourcePartId",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailHeight",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailIntervalSeconds",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteColumns",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteCount",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailSpriteVersion",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "VideoThumbnailWidth",
                table: "MediaParts");
        }
    }
}
