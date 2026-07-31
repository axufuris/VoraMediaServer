using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIptvChannelHealthCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "IptvHealthCheckTime",
                table: "ServerSettings",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "EnableHealthCheck",
                table: "IptvPlaylists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHealthy",
                table: "IptvChannels",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthCheckAt",
                table: "IptvChannels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                column: "IptvHealthCheckTime",
                value: new TimeSpan(0, 4, 30, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IptvHealthCheckTime",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EnableHealthCheck",
                table: "IptvPlaylists");

            migrationBuilder.DropColumn(
                name: "IsHealthy",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                table: "IptvChannels");
        }
    }
}
