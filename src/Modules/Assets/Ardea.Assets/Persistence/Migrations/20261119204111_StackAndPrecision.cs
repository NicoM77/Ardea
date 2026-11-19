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

            // The precision of the components there are, by the rule of VersionPrecisions.Of (ADR 046).
            migrationBuilder.Sql("""
                UPDATE assets.component SET version_precision = CASE
                    WHEN version IS NULL OR btrim(version) = '' THEN 'none'
                    WHEN purl IS NOT NULL THEN 'exact'
                    WHEN origin = 'repository' AND version ~ '^\d+(\.\d+)?$' THEN 'line'
                    WHEN version ~ '^(\d+|\d+(\.\d+)*\.[xX*])$' THEN 'line'
                    ELSE 'exact'
                END;
                """);

            // Every organisation has its stack (master plan 17.2); new ones get it when they are created. Iam migrates
            // before Assets (master plan 4.11); a test database with the Assets schema alone has no organisations.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('iam.organization') IS NOT NULL THEN
                        INSERT INTO assets.project (organization_id, kind, name, description, criticality, exposure, created_at, changed_at)
                        SELECT o.id, 'organization_stack', 'Stack', 'The technologies the organisation runs, without projects.', 'normal', 'internal', now(), now()
                        FROM iam.organization o
                        WHERE NOT EXISTS (SELECT 1 FROM assets.project p WHERE p.organization_id = o.id AND p.kind = 'organization_stack');
                    END IF;
                END $$;
                """);

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
                sql: "version_precision IN ('none', 'line', 'exact') AND (version_precision = 'none') = (version IS NULL OR btrim(version) = '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM assets.project WHERE kind = 'organization_stack';");

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
