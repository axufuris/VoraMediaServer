using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MediaExtraOwnsPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaExtras_FilePath",
                table: "MediaExtras");

            migrationBuilder.DropColumn(
                name: "DirectFilePath",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "Container",
                table: "MediaExtras");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "MediaExtras");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "MediaExtras");

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaItemId",
                table: "MediaParts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "MediaExtraId",
                table: "MediaParts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaParts_MediaExtraId",
                table: "MediaParts",
                column: "MediaExtraId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaParts_MediaExtras_MediaExtraId",
                table: "MediaParts",
                column: "MediaExtraId",
                principalTable: "MediaExtras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaParts_MediaExtras_MediaExtraId",
                table: "MediaParts");

            migrationBuilder.DropIndex(
                name: "IX_MediaParts_MediaExtraId",
                table: "MediaParts");

            migrationBuilder.DropColumn(
                name: "MediaExtraId",
                table: "MediaParts");

            migrationBuilder.AddColumn<string>(
                name: "DirectFilePath",
                table: "StreamSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaItemId",
                table: "MediaParts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Container",
                table: "MediaExtras",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "MediaExtras",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "MediaExtras",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaExtras_FilePath",
                table: "MediaExtras",
                column: "FilePath");
        }
    }
}
