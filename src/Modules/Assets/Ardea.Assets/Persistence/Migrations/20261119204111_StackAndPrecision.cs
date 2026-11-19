using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StackAndPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "assets",
                table: "project",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "project");

            migrationBuilder.AddColumn<string>(
                name: "notice_threshold",
                schema: "assets",
                table: "component",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "only_important");

            migrationBuilder.AddColumn<string>(
                name: "version_precision",
                schema: "assets",
                table: "component",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "none");

            // The rule of VersionPrecisions.Of (ADR 046) in SQL: the backfill of the components there are, and, for this version
            // only, a trigger that fills the precision when a writer leaves it unfitting. The API and worker of the previous
            // version write components without it during the upgrade (master plan 4.11); the next version drops the trigger.
            // Blank and trimmed mean what C# trims ([[:space:]]: spaces, tabs, line breaks), as in the check constraint.
            migrationBuilder.Sql("""
                CREATE FUNCTION assets.version_precision_of(version text, purl text, origin text)
                    RETURNS text LANGUAGE sql IMMUTABLE SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT CASE
                            WHEN version IS NULL OR version ~ '^[[:space:]]*$' THEN 'none'
                            WHEN purl IS NOT NULL THEN 'exact'
                            WHEN origin = 'repository' AND version ~ '^[[:space:]]*\d+(\.\d+)?[[:space:]]*$' THEN 'line'
                            WHEN version ~ '^[[:space:]]*(\d+|\d+(\.\d+)*\.[xX*])[[:space:]]*$' THEN 'line'
                            ELSE 'exact'
                        END
                    $$;

                UPDATE assets.component SET version_precision = assets.version_precision_of(version, purl, origin);

                CREATE FUNCTION assets.component_fill_precision()
                    RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        IF (NEW.version_precision = 'none') <> (NEW.version IS NULL OR NEW.version ~ '^[[:space:]]*$') THEN
                            NEW.version_precision := assets.version_precision_of(NEW.version, NEW.purl, NEW.origin);
                        END IF;
                        RETURN NEW;
                    END
                    $$;
                CREATE TRIGGER component_fill_precision BEFORE INSERT OR UPDATE ON assets.component
                    FOR EACH ROW EXECUTE FUNCTION assets.component_fill_precision();
                """);

            // Every organisation's stack (master plan 17.2) comes from the module's master-data step, which runs after every
            // migrate (AssetsModule): organisations the previous API creates during the upgrade get theirs on the next one.

            migrationBuilder.CreateIndex(
                name: "ux_project_stack",
                schema: "assets",
                table: "project",
                column: "organization_id",
                unique: true,
                filter: "kind = 'organization_stack'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_kind",
                schema: "assets",
                table: "project",
                sql: "kind IN ('project', 'organization_stack')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_component_notice_threshold",
                schema: "assets",
                table: "component",
                sql: "notice_threshold IN ('only_important', 'everything')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_component_version_precision",
                schema: "assets",
                table: "component",
                sql: "version_precision IN ('none', 'line', 'exact') AND (version_precision = 'none') = (version IS NULL OR version ~ '^[[:space:]]*$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER component_fill_precision ON assets.component;
                DROP FUNCTION assets.component_fill_precision();
                DROP FUNCTION assets.version_precision_of(text, text, text);
                DELETE FROM assets.project WHERE kind = 'organization_stack';
                """);

            migrationBuilder.DropIndex(
                name: "ux_project_stack",
                schema: "assets",
                table: "project");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_kind",
                schema: "assets",
                table: "project");

            migrationBuilder.DropCheckConstraint(
                name: "ck_component_notice_threshold",
                schema: "assets",
                table: "component");

            migrationBuilder.DropCheckConstraint(
                name: "ck_component_version_precision",
                schema: "assets",
                table: "component");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "assets",
                table: "project");

            migrationBuilder.DropColumn(
                name: "notice_threshold",
                schema: "assets",
                table: "component");

            migrationBuilder.DropColumn(
                name: "version_precision",
                schema: "assets",
                table: "component");
        }
    }
}
