using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assess");

            migrationBuilder.CreateTable(
                name: "alert",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    assessed_entry_revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert", x => x.id);
                    table.CheckConstraint("ck_alert_level", "level BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "finding",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    version_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    fixed_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verdict_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status_changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding", x => x.id);
                    table.CheckConstraint("ck_finding_level", "level BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_finding_match_type", "match_type IN ('package', 'cpe', 'product', 'variant', 'ecosystem', 'name')");
                    table.CheckConstraint("ck_finding_status", "status IN ('open', 'in_review', 'remediated', 'risk_accepted', 'not_affected')");
                    table.CheckConstraint("ck_finding_verdict", "verdict IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected')");
                    table.CheckConstraint("ck_finding_verdict_source", "verdict_source IN ('rules', 'model', 'human')");
                    table.CheckConstraint("ck_finding_version_verdict", "version_verdict IS NULL OR version_verdict IN ('in_range', 'out_of_range', 'unknown')");
                    table.ForeignKey(
                        name: "fk_finding_alert_alert_id",
                        column: x => x.alert_id,
                        principalSchema: "assess",
                        principalTable: "alert",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_entry",
                schema: "assess",
                table: "alert",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_alert_entry",
                schema: "assess",
                table: "alert",
                columns: new[] { "organization_id", "entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_finding_component",
                schema: "assess",
                table: "finding",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_project_entry",
                schema: "assess",
                table: "finding",
                columns: new[] { "project_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ux_finding_component",
                schema: "assess",
                table: "finding",
                columns: new[] { "alert_id", "component_id" },
                unique: true);

            migrationBuilder.Sql("""
                -- The entry is global data; the key keeps alerts consistent without coupling the modules in code (master plan 4.1).
                ALTER TABLE assess.alert ADD CONSTRAINT fk_alert_security_entry FOREIGN KEY (entry_id) REFERENCES intel.security_entry (id);

                ALTER TABLE assess.alert ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.alert
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assess.finding ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.finding
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                -- System operation "reassessment, withdrawal of an entry" (master plan 9.2, ADR 029): which
                -- organisations have an alert for this entry. Returns organisation ids only.
                CREATE FUNCTION assess.organizations_with_alert(p_entry_id uuid)
                    RETURNS TABLE (organization_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT a.organization_id FROM assess.alert a WHERE a.entry_id = p_entry_id
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION assess.organizations_with_alert(uuid);");

            migrationBuilder.DropTable(
                name: "finding",
                schema: "assess");

            migrationBuilder.DropTable(
                name: "alert",
                schema: "assess");
        }
    }
}
