using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Actions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "closed_level",
                schema: "assess",
                table: "finding",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closed_version_verdict",
                schema: "assess",
                table: "finding",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exploitation",
                schema: "assess",
                table: "finding",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<bool>(
                name: "fix_available",
                schema: "assess",
                table: "finding",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "remediation_id",
                schema: "assess",
                table: "finding",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "entry_key",
                schema: "assess",
                table: "alert",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_raised_at",
                schema: "assess",
                table: "alert",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "number",
                schema: "assess",
                table: "alert",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assess_event",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assess_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "match_scope",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_scope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_counter",
                schema: "assess",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_counter", x => x.organization_id);
                });

            migrationBuilder.CreateTable(
                name: "remediation",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    target_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    open_findings = table.Column<int>(type: "integer", nullable: false),
                    exploitation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    fix_available = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at_manual = table.Column<bool>(type: "boolean", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level_raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remediation", x => x.id);
                    table.CheckConstraint("ck_remediation_exploitation", "exploitation IN ('none', 'poc', 'active')");
                    table.CheckConstraint("ck_remediation_level", "level BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_remediation_status", "status IN ('open', 'in_review', 'closed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding",
                columns: new[] { "organization_id", "level" });

            migrationBuilder.CreateIndex(
                name: "ix_finding_remediation",
                schema: "assess",
                table: "finding",
                column: "remediation_id");

            migrationBuilder.CreateIndex(
                name: "ux_alert_number",
                schema: "assess",
                table: "alert",
                columns: new[] { "organization_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assess_event_finding",
                schema: "assess",
                table: "assess_event",
                column: "finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_assess_event_org_at",
                schema: "assess",
                table: "assess_event",
                columns: new[] { "organization_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_assess_event_remediation",
                schema: "assess",
                table: "assess_event",
                columns: new[] { "remediation_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_match_scope",
                schema: "assess",
                table: "match_scope",
                columns: new[] { "project_id", "repository_link_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_remediation_list",
                schema: "assess",
                table: "remediation",
                columns: new[] { "organization_id", "status", "level", "number" });

            migrationBuilder.CreateIndex(
                name: "ux_remediation_component_open",
                schema: "assess",
                table: "remediation",
                column: "component_id",
                unique: true,
                filter: "status <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "ux_remediation_number",
                schema: "assess",
                table: "remediation",
                columns: new[] { "organization_id", "number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_finding_remediations_remediation_id",
                schema: "assess",
                table: "finding",
                column: "remediation_id",
                principalSchema: "assess",
                principalTable: "remediation",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                UPDATE assess.alert a SET entry_key = e.primary_key FROM intel.security_entry e WHERE e.id = a.entry_id;

                ALTER TABLE assess.remediation ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.remediation
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assess.assess_event ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.assess_event
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assess.org_counter ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.org_counter
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assess.match_scope ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.match_scope
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_finding_remediations_remediation_id",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropTable(
                name: "assess_event",
                schema: "assess");

            migrationBuilder.DropTable(
                name: "match_scope",
                schema: "assess");

            migrationBuilder.DropTable(
                name: "org_counter",
                schema: "assess");

            migrationBuilder.DropTable(
                name: "remediation",
                schema: "assess");

            migrationBuilder.DropIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropIndex(
                name: "ix_finding_remediation",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropIndex(
                name: "ux_alert_number",
                schema: "assess",
                table: "alert");

            migrationBuilder.DropColumn(
                name: "closed_level",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "closed_version_verdict",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "exploitation",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "fix_available",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "remediation_id",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "entry_key",
                schema: "assess",
                table: "alert");

            migrationBuilder.DropColumn(
                name: "first_raised_at",
                schema: "assess",
                table: "alert");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "assess",
                table: "alert");
        }
    }
}
