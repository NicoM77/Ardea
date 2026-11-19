using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscription",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    endpoint_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auth = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscription", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_push_subscription",
                schema: "notify",
                table: "push_subscription",
                columns: new[] { "user_id", "endpoint_hash" },
                unique: true);

            migrationBuilder.Sql("""
                -- User data without an organisation (master plan 4.1, 9.2; ADR 040). The user reads and writes their own
                -- devices. The worker, in a unit of work of an organisation without a user, reads, counts and removes the
                -- devices of a named recipient who is a member of that organisation, and nothing else.
                ALTER TABLE notify.push_subscription ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON notify.push_subscription
                    USING (user_id = platform.current_user_id())
                    WITH CHECK (user_id = platform.current_user_id());
                CREATE POLICY deliver_read ON notify.push_subscription FOR SELECT
                    USING (platform.current_user_id() IS NULL
                        AND user_id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()));
                CREATE POLICY deliver_update ON notify.push_subscription FOR UPDATE
                    USING (platform.current_user_id() IS NULL
                        AND user_id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()))
                    WITH CHECK (platform.current_user_id() IS NULL
                        AND user_id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()));
                CREATE POLICY deliver_delete ON notify.push_subscription FOR DELETE
                    USING (platform.current_user_id() IS NULL
                        AND user_id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()));

                -- Default rule 1 of master plan 8.3 reaches owners and admins by push too (ADR 038 decision 11), also in
                -- organisations created before the channel existed. A member without a device is not reached by it.
                INSERT INTO notify.rule_target (organization_id, rule_id, channel, recipient_kind, recipient_ref, mode, digest_cron, send_empty, created_at)
                SELECT r.organization_id, r.id, 'push', 'role', 'admin', 'immediate', NULL, false, now()
                FROM notify.rule r
                WHERE r.preset = 'critical'
                    AND NOT EXISTS (SELECT 1 FROM notify.rule_target t WHERE t.rule_id = r.id AND t.channel = 'push');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM notify.rule_target WHERE channel = 'push';");

            migrationBuilder.DropTable(
                name: "push_subscription",
                schema: "notify");
        }
    }
}
