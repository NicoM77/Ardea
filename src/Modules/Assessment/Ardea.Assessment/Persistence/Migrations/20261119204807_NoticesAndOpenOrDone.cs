using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <summary>
    /// Task 5a.2 (master plan 17.5, 17.6; ADR 046): the table of notices; actions and findings are open or done, without
    /// «in review»; open actions on components without a version become notices. The columns <c>assigned_to</c>,
    /// <c>due_at</c> and <c>due_at_manual</c> of <c>assess.remediation</c> are no longer mapped, written or read; by the
    /// compatibility rule of master plan 4.11 a migration of the next version drops them.
    /// </summary>
    public partial class NoticesAndOpenOrDone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_remediation_status",
                schema: "assess",
                table: "remediation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_status",
                schema: "assess",
                table: "finding");

            // «In review» is gone: what was in review is open (master plan 17.5).
            migrationBuilder.Sql("""
                UPDATE assess.remediation SET status = 'open' WHERE status = 'in_review';
                UPDATE assess.finding SET status = 'open' WHERE status = 'in_review';

                -- Unused from now on; inserts no longer name it (master plan 4.11: dropped one version later).
                ALTER TABLE assess.remediation ALTER COLUMN due_at_manual SET DEFAULT false;

                -- For this version only (master plan 4.11): the API of the previous version may still take an action into
                -- review during the upgrade; what it writes as «in review» is stored as open. The next version drops it.
                CREATE FUNCTION assess.open_instead_of_review()
                    RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        IF NEW.status = 'in_review' THEN
                            NEW.status := 'open';
                        END IF;
                        RETURN NEW;
                    END
                    $$;
                CREATE TRIGGER remediation_open_instead_of_review BEFORE INSERT OR UPDATE OF status ON assess.remediation
                    FOR EACH ROW EXECUTE FUNCTION assess.open_instead_of_review();
                CREATE TRIGGER finding_open_instead_of_review BEFORE INSERT OR UPDATE OF status ON assess.finding
                    FOR EACH ROW EXECUTE FUNCTION assess.open_instead_of_review();
                """);

            migrationBuilder.CreateTable(
                name: "notice",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    component_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reappeared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reappeared_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice", x => x.id);
                    table.CheckConstraint("ck_notice_decided", "state = 'open' OR (decided_by IS NOT NULL AND decided_at IS NOT NULL)");
                    table.CheckConstraint("ck_notice_reappeared", "reappeared_reason IS NULL OR reappeared_reason IN ('exploited', 'kev')");
                    table.CheckConstraint("ck_notice_state", "state IN ('open', 'affects_us', 'not_affected')");
                    table.ForeignKey(
                        name: "fk_notice_remediation_remediation_id",
                        column: x => x.remediation_id,
                        principalSchema: "assess",
                        principalTable: "remediation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notice_list",
                schema: "assess",
                table: "notice",
                columns: new[] { "organization_id", "state", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notice_remediation",
                schema: "assess",
                table: "notice",
                column: "remediation_id");

            migrationBuilder.CreateIndex(
                name: "ux_notice_entry",
                schema: "assess",
                table: "notice",
                columns: new[] { "organization_id", "entry_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE assess.notice ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.notice
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                """);

            // E25 (master plan 17.6, ADR 046): an open action on a component without a version becomes a notice per entry of
            // its open findings, with the component as the reason. The findings close with the reason «moved_to_notice» at
            // level 1, the action closes with it, and both histories say so. Closed actions and actions on components with a
            // version stay as they are. Assets migrates before Assess (master plan 4.11), so the precision is there.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    -- A test database with the Assess schema but without Assets has no components, and nothing to move.
                    IF to_regclass('assets.component') IS NULL THEN
                        RETURN;
                    END IF;

                    -- Cross-schema read (ADR 046): Assets must have migrated first (Modules.cs order, master plan 4.11).
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_schema = 'assets' AND table_name = 'component' AND column_name = 'version_precision') THEN
                        RAISE EXCEPTION 'assets.component.version_precision is missing: migrate Assets (StackAndPrecision) before Assess (NoticesAndOpenOrDone)';
                    END IF;

                    CREATE TEMPORARY TABLE moved_action AS
                        SELECT r.id, r.organization_id, r.opened_at
                        FROM assess.remediation r JOIN assets.component c ON c.id = r.component_id
                        WHERE r.status <> 'closed' AND c.version_precision = 'none';

                    CREATE TEMPORARY TABLE moved_finding AS
                        SELECT f.id, f.organization_id, f.alert_id, f.entry_id, f.component_id, f.remediation_id, m.opened_at
                        FROM assess.finding f JOIN moved_action m ON m.id = f.remediation_id
                        WHERE f.status = 'open';

                    INSERT INTO assess.notice (organization_id, entry_id, entry_key, state, component_ids, raised_at, changed_at)
                    SELECT f.organization_id, f.entry_id, a.entry_key, 'open', array_agg(DISTINCT f.component_id), min(f.opened_at), now()
                    FROM moved_finding f JOIN assess.alert a ON a.id = f.alert_id
                    GROUP BY f.organization_id, f.entry_id, a.entry_key
                    ON CONFLICT (organization_id, entry_id) DO NOTHING;

                    INSERT INTO assess.assess_event (organization_id, remediation_id, finding_id, at, kind, data)
                    SELECT organization_id, remediation_id, id, now(), 'finding.closed', '{"reason":"moved_to_notice","status":"not_affected"}'::jsonb
                    FROM moved_finding;

                    UPDATE assess.finding f
                    SET status = 'not_affected', status_reason = 'moved_to_notice', status_changed_by = NULL, status_changed_at = now(), changed_at = now(),
                        level = 1,
                        factors = jsonb_build_object(
                            'rulesetVersion', COALESCE((f.factors->>'rulesetVersion')::int, 1), 'level', 1, 'exclusion', 'moved_to_notice',
                            'factors', '[]'::jsonb, 'caps', '[]'::jsonb, 'floors', '[]'::jsonb)
                    FROM moved_finding m WHERE m.id = f.id;

                    -- History only: rows written here reach no notification (only the units of work hand events on).
                    INSERT INTO assess.assess_event (organization_id, remediation_id, at, kind, data)
                    SELECT organization_id, id, now(), 'action.withdrawn', '{"reason":"moved_to_notice"}'::jsonb
                    FROM moved_action;

                    UPDATE assess.remediation r
                    SET status = 'closed', closed_at = now(), close_reason = 'moved_to_notice', open_findings = 0, changed_at = now()
                    FROM moved_action m WHERE m.id = r.id;

                    UPDATE assess.alert a
                    SET level = (SELECT max(f.level) FROM assess.finding f WHERE f.alert_id = a.id), changed_at = now()
                    WHERE a.id IN (SELECT alert_id FROM moved_finding);

                    DROP TABLE moved_finding, moved_action;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_remediation_status",
                schema: "assess",
                table: "remediation",
                sql: "status IN ('open', 'closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_status",
                schema: "assess",
                table: "finding",
                sql: "status IN ('open', 'remediated', 'risk_accepted', 'not_affected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The notices and the closed actions stay closed; a restore of the dump «migrate» wrote brings the old state back.
            migrationBuilder.DropTable(
                name: "notice",
                schema: "assess");

            migrationBuilder.DropCheckConstraint(
                name: "ck_remediation_status",
                schema: "assess",
                table: "remediation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_status",
                schema: "assess",
                table: "finding");

            migrationBuilder.Sql("""
                DROP TRIGGER remediation_open_instead_of_review ON assess.remediation;
                DROP TRIGGER finding_open_instead_of_review ON assess.finding;
                DROP FUNCTION assess.open_instead_of_review();
                ALTER TABLE assess.remediation ALTER COLUMN due_at_manual DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_remediation_status",
                schema: "assess",
                table: "remediation",
                sql: "status IN ('open', 'in_review', 'closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_status",
                schema: "assess",
                table: "finding",
                sql: "status IN ('open', 'in_review', 'remediated', 'risk_accepted', 'not_affected')");
        }
    }
}
