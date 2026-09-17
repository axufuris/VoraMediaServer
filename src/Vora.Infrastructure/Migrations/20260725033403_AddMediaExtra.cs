using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaExtra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectFilePath",
                table: "StreamSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExtraId",
                table: "StreamSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaExtras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExtraType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Container = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    MediaItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaExtras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaExtras_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaExtras_FilePath",
                table: "MediaExtras",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_MediaExtras_MediaItemId",
                table: "MediaExtras",
                column: "MediaItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaExtras");

            migrationBuilder.DropColumn(
                name: "DirectFilePath",
                table: "StreamSessions");

            migrationBuilder.DropColumn(
                name: "ExtraId",
                table: "StreamSessions");
        }
    }
}
