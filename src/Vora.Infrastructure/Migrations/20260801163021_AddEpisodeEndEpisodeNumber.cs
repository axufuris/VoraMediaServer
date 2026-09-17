using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeEndEpisodeNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EndEpisodeNumber",
                table: "MediaItems",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""MediaItems"" AS ep
                SET ""EndEpisodeNumber"" = sub.endep
                FROM (
                    SELECT mp.""MediaItemId"" AS id,
                           (substring(mp.""FilePath"" FROM '[Ss][0-9]{1,4}[Ee][0-9]{1,4}\s*-\s*[Ee]([0-9]{1,4})'))::int AS endep
                    FROM ""MediaParts"" mp
                ) AS sub
                WHERE ep.""Id"" = sub.id
                  AND ep.""MediaType"" = 'Episode'
                  AND sub.endep IS NOT NULL
                  AND sub.endep > ep.""EpisodeNumber"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndEpisodeNumber",
                table: "MediaItems");
        }
    }
}
