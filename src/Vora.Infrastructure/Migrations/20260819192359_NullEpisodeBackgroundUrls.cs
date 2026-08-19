using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NullEpisodeBackgroundUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"MediaItems\" SET \"BackgroundUrl\" = NULL WHERE \"MediaType\" = 'Episode' AND \"BackgroundUrl\" IS NOT NULL AND \"BackgroundUrl\" = \"PosterUrl\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
