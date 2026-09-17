using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSystemListEpisodeDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("17ddede2-2de0-42b8-9b33-32708b4d29b8"),
                column: "FilterRulesJson",
                value: "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\",\"Episode\"]}");

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("58424b85-b6da-4a9c-8204-e364f1319508"),
                columns: new[] { "FilterRulesJson", "Title" },
                values: new object[] { "{\"mediaTypes\":[\"Episode\"]}", "Recently Released Episodes" });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                columns: new[] { "FilterRulesJson", "Title" },
                values: new object[] { "{\"mediaTypes\":[\"Movie\",\"Episode\"]}", "Recently Released Movies & Episodes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("17ddede2-2de0-42b8-9b33-32708b4d29b8"),
                column: "FilterRulesJson",
                value: "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}");

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("58424b85-b6da-4a9c-8204-e364f1319508"),
                columns: new[] { "FilterRulesJson", "Title" },
                values: new object[] { "{\"mediaTypes\":[\"TvShow\"]}", "Recently Released Shows" });

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                columns: new[] { "FilterRulesJson", "Title" },
                values: new object[] { "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\"]}", "Recently Released Movies & Shows" });
        }
    }
}
