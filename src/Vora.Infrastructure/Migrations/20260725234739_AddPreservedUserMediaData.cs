using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreservedUserMediaData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreservedUserMediaData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Rating = table.Column<decimal>(type: "numeric", nullable: true),
                    RatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HasState = table.Column<bool>(type: "boolean", nullable: false),
                    ResumePositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsPlayed = table.Column<bool>(type: "boolean", nullable: false),
                    IsHiddenFromContinueWatching = table.Column<bool>(type: "boolean", nullable: false),
                    LastPlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreservedUserMediaData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreservedUserMediaData_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreservedUserMediaData_ContentKey",
                table: "PreservedUserMediaData",
                column: "ContentKey");

            migrationBuilder.CreateIndex(
                name: "IX_PreservedUserMediaData_ProfileId_ContentKey",
                table: "PreservedUserMediaData",
                columns: new[] { "ProfileId", "ContentKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreservedUserMediaData");
        }
    }
}
