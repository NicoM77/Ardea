using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
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
                ALTER TABLE notify.reminder_schedule ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.reminder_schedule
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.reminder_state ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.reminder_state
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION notify.due_reminder_runs(timestamptz);
                DELETE FROM notify.delivery WHERE kind = 'reminder';
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
