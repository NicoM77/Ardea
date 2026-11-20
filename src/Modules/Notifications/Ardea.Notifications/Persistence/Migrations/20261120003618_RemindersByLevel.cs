using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <summary>
    /// Task 5a.6 (master plan 8.7, 17.5; ADR 048): the reminders by level get their schedule per organisation and the time
    /// each open reminder last reminded; a run's messages are deliveries of the kind <c>reminder</c>. A notice is announced
    /// once per recipient and channel (<c>delivery_event.notice_id</c>); the default rules learn <c>notice.raised</c> in the
    /// module's step after every migrate.
    /// Only additions and a widened check: the previous build runs against this schema (master plan 4.11).
    /// </summary>
    public partial class RemindersByLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_delivery_kind",
                schema: "notify",
                table: "delivery");

            migrationBuilder.AddColumn<Guid>(
                name: "notice_id",
                schema: "notify",
                table: "delivery_event",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reminder_schedule",
                schema: "notify",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level5 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    level4 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    level3 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_schedule", x => x.organization_id);
                    table.CheckConstraint("ck_reminder_schedule_level3", "level3 IN ('daily', 'weekly', 'monthly', 'off')");
                    table.CheckConstraint("ck_reminder_schedule_level4", "level4 IN ('daily', 'weekly', 'monthly', 'off')");
                    table.CheckConstraint("ck_reminder_schedule_level5", "level5 IN ('daily', 'weekly', 'monthly', 'off')");
                });

            migrationBuilder.CreateTable(
                name: "reminder_state",
                schema: "notify",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_state", x => new { x.organization_id, x.remediation_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_event_notice",
                schema: "notify",
                table: "delivery_event",
                columns: new[] { "organization_id", "notice_id", "recipient_key", "channel" },
                filter: "notice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_reminder",
                schema: "notify",
                table: "delivery",
                columns: new[] { "organization_id", "recipient_key", "channel", "period_end" },
                unique: true,
                filter: "kind = 'reminder'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_delivery_kind",
                schema: "notify",
                table: "delivery",
                sql: "kind IN ('immediate', 'digest', 'reminder')");

            migrationBuilder.Sql("""
                -- Organisation data (master plan 4.1, 9.2; ADR 029).
                ALTER TABLE notify.reminder_schedule ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.reminder_schedule
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.reminder_state ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.reminder_state
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                -- System operation "reminders by level" (master plan 9.2, ADR 029, ADR 048): the organisations whose run is
                -- due, or that have rules but no schedule yet (their first run writes the defaults). Organisation ids only.
                CREATE FUNCTION notify.due_reminder_runs(p_now timestamptz)
                    RETURNS TABLE (organization_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT s.organization_id FROM notify.reminder_schedule s
                        WHERE s.next_run_at IS NULL OR s.next_run_at <= p_now
                        UNION
                        SELECT DISTINCT r.organization_id FROM notify.rule r
                        WHERE NOT EXISTS (SELECT 1 FROM notify.reminder_schedule s WHERE s.organization_id = r.organization_id)
                    $$;
                """);

            // The default rules that nobody edited learn notice.raised in the module's step after every migrate
            // (DefaultRules.NameNoticesAsync), so organisations the previous build creates during an upgrade get it too.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION notify.due_reminder_runs(timestamptz);
                DELETE FROM notify.delivery WHERE kind = 'reminder';
                -- A rule that names nothing but notices is an organisation's own: it is switched off, not deleted; the build
                -- before ignores the event it names. The others forget it.
                UPDATE notify.rule SET enabled = false WHERE events <@ ARRAY['notice.raised']::text[] AND cardinality(events) > 0;
                UPDATE notify.rule SET events = array_remove(events, 'notice.raised')
                WHERE 'notice.raised' = ANY (events) AND NOT (events <@ ARRAY['notice.raised']::text[]);
                """);

            migrationBuilder.DropTable(
                name: "reminder_schedule",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "reminder_state",
                schema: "notify");

            migrationBuilder.DropIndex(
                name: "ix_delivery_event_notice",
                schema: "notify",
                table: "delivery_event");

            migrationBuilder.DropIndex(
                name: "ux_delivery_reminder",
                schema: "notify",
                table: "delivery");

            migrationBuilder.DropCheckConstraint(
                name: "ck_delivery_kind",
                schema: "notify",
                table: "delivery");

            migrationBuilder.DropColumn(
                name: "notice_id",
                schema: "notify",
                table: "delivery_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_delivery_kind",
                schema: "notify",
                table: "delivery",
                sql: "kind IN ('immediate', 'digest')");
        }
    }
}
