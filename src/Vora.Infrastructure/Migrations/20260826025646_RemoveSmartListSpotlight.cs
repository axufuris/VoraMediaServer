using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSmartListSpotlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSpotlight",
                table: "SmartLists");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSpotlight",
                table: "SmartLists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("17ddede2-2de0-42b8-9b33-32708b4d29b8"),
                column: "IsSpotlight",
                value: false);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("58424b85-b6da-4a9c-8204-e364f1319508"),
                column: "IsSpotlight",
                value: false);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                column: "IsSpotlight",
                value: true);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("c88d6c8a-57ea-4b24-a7be-3f2638a38aca"),
                column: "IsSpotlight",
                value: false);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("dfc420d4-421c-4e14-aec4-a5bedefd2f2e"),
                column: "IsSpotlight",
                value: false);

            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("ebbefd92-4232-4cae-9c5d-2134943b8bf8"),
                column: "IsSpotlight",
                value: false);
        }
    }
}
