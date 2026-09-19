using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseDatesAsDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns held a calendar date stored as midnight UTC. A bare
            // ALTER ... TYPE date would cast in the server's own timezone, which
            // moves every value to the previous day west of Greenwich, so the cast
            // reads them back as UTC first.
            migrationBuilder.Sql("ALTER TABLE \"UserWatchlistItems\" ALTER COLUMN \"ExpectedReleaseDate\" TYPE date USING (\"ExpectedReleaseDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaRequests\" ALTER COLUMN \"ExpectedReleaseDate\" TYPE date USING (\"ExpectedReleaseDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"TheatricalReleaseDate\" TYPE date USING (\"TheatricalReleaseDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"ReleaseDate\" TYPE date USING (\"ReleaseDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"NextAirDate\" TYPE date USING (\"NextAirDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"LastAirDate\" TYPE date USING (\"LastAirDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"DigitalReleaseDate\" TYPE date USING (\"DigitalReleaseDate\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"Actors\" ALTER COLUMN \"Deathday\" TYPE date USING (\"Deathday\" AT TIME ZONE 'UTC')::date;");
            migrationBuilder.Sql("ALTER TABLE \"Actors\" ALTER COLUMN \"Birthday\" TYPE date USING (\"Birthday\" AT TIME ZONE 'UTC')::date;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"UserWatchlistItems\" ALTER COLUMN \"ExpectedReleaseDate\" TYPE timestamp with time zone USING \"ExpectedReleaseDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaRequests\" ALTER COLUMN \"ExpectedReleaseDate\" TYPE timestamp with time zone USING \"ExpectedReleaseDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"TheatricalReleaseDate\" TYPE timestamp with time zone USING \"TheatricalReleaseDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"ReleaseDate\" TYPE timestamp with time zone USING \"ReleaseDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"NextAirDate\" TYPE timestamp with time zone USING \"NextAirDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"LastAirDate\" TYPE timestamp with time zone USING \"LastAirDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"MediaItems\" ALTER COLUMN \"DigitalReleaseDate\" TYPE timestamp with time zone USING \"DigitalReleaseDate\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"Actors\" ALTER COLUMN \"Deathday\" TYPE timestamp with time zone USING \"Deathday\"::timestamp AT TIME ZONE 'UTC';");
            migrationBuilder.Sql("ALTER TABLE \"Actors\" ALTER COLUMN \"Birthday\" TYPE timestamp with time zone USING \"Birthday\"::timestamp AT TIME ZONE 'UTC';");
        }
    }
}
