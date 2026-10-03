using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoreSongProfileListsAsArrays : Migration
    {
        private const string Moods =
            "'happy','upbeat','euphoric','playful','uplifting','confident'," +
            "'romantic','sensual','groovy','chill','calm','dreamy'," +
            "'nostalgic','bittersweet','melancholy','sad'," +
            "'dark','mysterious','intense','aggressive','rebellious','epic'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION vora_json_text_array(value text) RETURNS text[] LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE
                        WHEN value IS NULL OR btrim(value) = '' THEN NULL
                        ELSE ARRAY(SELECT jsonb_array_elements_text(value::jsonb))
                    END
                $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MediaItems"
                    ALTER COLUMN "Moods" TYPE text[] USING vora_json_text_array("Moods"),
                    ALTER COLUMN "Themes" TYPE text[] USING vora_json_text_array("Themes"),
                    ALTER COLUMN "GoodFor" TYPE text[] USING vora_json_text_array("GoodFor");
                """);

            migrationBuilder.Sql("DROP FUNCTION vora_json_text_array(text);");

            migrationBuilder.Sql(
                "UPDATE \"MediaItems\" SET \"ProfiledAt\" = NULL " +
                $"WHERE \"Moods\" IS NOT NULL AND NOT (\"Moods\" <@ ARRAY[{Moods}]::text[]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MediaItems"
                    ALTER COLUMN "Moods" TYPE character varying(512) USING CASE WHEN "Moods" IS NULL THEN NULL ELSE to_jsonb("Moods")::text END,
                    ALTER COLUMN "Themes" TYPE character varying(512) USING CASE WHEN "Themes" IS NULL THEN NULL ELSE to_jsonb("Themes")::text END,
                    ALTER COLUMN "GoodFor" TYPE character varying(512) USING CASE WHEN "GoodFor" IS NULL THEN NULL ELSE to_jsonb("GoodFor")::text END;
                """);
        }
    }
}
