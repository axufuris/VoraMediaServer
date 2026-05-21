using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailEnabled",
                table: "ServerSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailPublicBaseUrl",
                table: "ServerSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromAddress",
                table: "ServerSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromDisplayName",
                table: "ServerSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "ServerSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPasswordCiphertext",
                table: "ServerSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "ServerSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseImplicitSsl",
                table: "ServerSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseStartTls",
                table: "ServerSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "ServerSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailDeliveryLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectOverride = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HtmlBodyOverride = table.Column<string>(type: "text", nullable: true),
                    TextBodyOverride = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Key);
                });

            migrationBuilder.UpdateData(
                table: "ServerSettings",
                keyColumn: "Id",
                keyValue: "GLOBAL_SETTINGS",
                columns: new[] { "EmailEnabled", "EmailPublicBaseUrl", "SmtpFromAddress", "SmtpFromDisplayName", "SmtpHost", "SmtpPasswordCiphertext", "SmtpPort", "SmtpUseImplicitSsl", "SmtpUseStartTls", "SmtpUsername" },
                values: new object[] { false, null, null, null, null, null, 587, false, true, null });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryLogs_CreatedAt",
                table: "EmailDeliveryLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryLogs_TemplateKey_CreatedAt",
                table: "EmailDeliveryLogs",
                columns: new[] { "TemplateKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDeliveryLogs");

            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "EmailEnabled",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "EmailPublicBaseUrl",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpFromAddress",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpFromDisplayName",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPasswordCiphertext",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUseImplicitSsl",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUseStartTls",
                table: "ServerSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "ServerSettings");
        }
    }
}
