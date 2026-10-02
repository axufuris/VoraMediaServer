using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAiPlaylistMatchCutoffWithWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AiPlaylistMatchCutoff",
                table: "ServerSettings",
                newName: "AiPlaylistMatchWindow");

            migrationBuilder.AlterColumn<double>(
                name: "AiPlaylistMatchWindow",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.04,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldDefaultValue: 0.55);

            migrationBuilder.Sql("UPDATE \"ServerSettings\" SET \"AiPlaylistMatchWindow\" = 0.04;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AiPlaylistMatchWindow",
                table: "ServerSettings",
                newName: "AiPlaylistMatchCutoff");

            migrationBuilder.AlterColumn<double>(
                name: "AiPlaylistMatchCutoff",
                table: "ServerSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.55,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldDefaultValue: 0.04);

            migrationBuilder.Sql("UPDATE \"ServerSettings\" SET \"AiPlaylistMatchCutoff\" = 0.55;");
        }
    }
}
