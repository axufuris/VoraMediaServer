using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPlaylistMixFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "GeneratedMixes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerProfileId",
                table: "GeneratedMixes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prompt",
                table: "GeneratedMixes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "GeneratedMixes");

            migrationBuilder.DropColumn(
                name: "PartnerProfileId",
                table: "GeneratedMixes");

            migrationBuilder.DropColumn(
                name: "Prompt",
                table: "GeneratedMixes");
        }
    }
}
