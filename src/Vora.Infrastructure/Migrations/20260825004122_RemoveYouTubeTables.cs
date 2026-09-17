using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveYouTubeTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YouTubeAccountSettings");

            migrationBuilder.DropTable(
                name: "YouTubeProfileSettings");

            migrationBuilder.DropTable(
                name: "YouTubeSubscriptions");

            migrationBuilder.DropTable(
                name: "YouTubeWatchHistory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YouTubeAccountSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    YouTubeAccess = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeAccountSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeProfileSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeProfileSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChannelThumbnailUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SubscribedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeWatchHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DurationWatched = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TotalDuration = table.Column<int>(type: "integer", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeWatchHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeAccountSettings_AccountId",
                table: "YouTubeAccountSettings",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeProfileSettings_UserProfileId",
                table: "YouTubeProfileSettings",
                column: "UserProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeSubscriptions_UserProfileId",
                table: "YouTubeSubscriptions",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeSubscriptions_UserProfileId_ChannelId",
                table: "YouTubeSubscriptions",
                columns: new[] { "UserProfileId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeWatchHistory_UserProfileId_VideoId",
                table: "YouTubeWatchHistory",
                columns: new[] { "UserProfileId", "VideoId" });

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeWatchHistory_UserProfileId_WatchedAt",
                table: "YouTubeWatchHistory",
                columns: new[] { "UserProfileId", "WatchedAt" });
        }
    }
}
