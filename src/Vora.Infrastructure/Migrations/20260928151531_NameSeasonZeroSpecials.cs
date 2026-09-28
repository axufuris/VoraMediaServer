using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NameSeasonZeroSpecials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ""MediaItems""
SET ""Title"" = 'Specials'
WHERE ""MediaType"" = 'Season' AND ""SeasonNumber"" = 0
  AND ""Title"" ~* '^season\s*0+$'
  AND COALESCE(""LockedFields"", '') NOT LIKE '%""Title""%';

UPDATE ""MediaItems""
SET ""SortTitle"" = 'Specials'
WHERE ""MediaType"" = 'Season' AND ""SeasonNumber"" = 0
  AND ""SortTitle"" ~* '^season\s*0+$'
  AND COALESCE(""LockedFields"", '') NOT LIKE '%""SortTitle""%';

UPDATE ""MediaItems""
SET ""OriginalTitle"" = 'Specials'
WHERE ""MediaType"" = 'Season' AND ""SeasonNumber"" = 0
  AND ""OriginalTitle"" ~* '^season\s*0+$'
  AND COALESCE(""LockedFields"", '') NOT LIKE '%""OriginalTitle""%';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
