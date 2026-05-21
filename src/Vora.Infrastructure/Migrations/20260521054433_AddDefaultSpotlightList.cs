using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultSpotlightList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                column: "IsSpotlight",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SmartLists",
                keyColumn: "Id",
                keyValue: new Guid("73c33c2c-1fe6-4885-875e-481a1dac5462"),
                column: "IsSpotlight",
                value: false);
        }
    }
}
