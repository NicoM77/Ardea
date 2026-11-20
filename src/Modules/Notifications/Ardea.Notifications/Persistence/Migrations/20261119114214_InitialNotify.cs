using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.CreateTable(
                name: "endpoint",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url_enc = table.Column<string>(type: "text", nullable: false),
                    secret_enc = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_status_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoint", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_address",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unsubscribed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_address", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_event",
                schema: "notify",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_event", x => new { x.organization_id, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "recipient_state",
                schema: "notify",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_level = table.Column<int>(type: "integer", nullable: false),
                    last_exploitation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    fix_notified = table.Column<bool>(type: "boolean", nullable: false),
                    last_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipient_state", x => new { x.organization_id, x.remediation_id, x.recipient_key, x.channel });
                    table.CheckConstraint("ck_recipient_state_exploitation", "last_exploitation IN ('none', 'poc', 'active')");
                });

            migrationBuilder.CreateTable(
                name: "rule",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    project_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    min_level = table.Column<int>(type: "integer", nullable: false),
                    events = table.Column<string[]>(type: "text[]", nullable: false),
                    preset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule", x => x.id);
                    table.CheckConstraint("ck_rule_min_level", "min_level BETWEEN 2 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "user_preference",
                schema: "notify",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    muted_channels = table.Column<string[]>(type: "text[]", nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preference", x => new { x.user_id, x.organization_id });
                });

            migrationBuilder.CreateTable(
                name: "rule_target",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    digest_cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    send_empty = table.Column<bool>(type: "boolean", nullable: false),
                    next_digest_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_digest_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_target", x => x.id);
                    table.CheckConstraint("ck_rule_target_channel", "channel IN ('in_app', 'email', 'webhook', 'push')");
                    table.CheckConstraint("ck_rule_target_mode", "mode IN ('immediate', 'digest') AND (mode = 'immediate' OR digest_cron IS NOT NULL)");
                    table.CheckConstraint("ck_rule_target_recipient", "recipient_kind IN ('role', 'user', 'address', 'endpoint')");
                    table.ForeignKey(
                        name: "fk_rule_target_rule_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "notify",
                        principalTable: "rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    send_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery", x => x.id);
                    table.CheckConstraint("ck_delivery_kind", "kind IN ('immediate', 'digest')");
                    table.CheckConstraint("ck_delivery_state", "state IN ('pending', 'sending', 'sent', 'failed', 'dead', 'suppressed')");
                    table.ForeignKey(
                        name: "fk_delivery_rule_target_rule_target_id",
                        column: x => x.rule_target_id,
                        principalSchema: "notify",
                        principalTable: "rule_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "digest_item",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_digest_item_rule_target_rule_target_id",
                        column: x => x.rule_target_id,
                        principalSchema: "notify",
                        principalTable: "rule_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_event",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    remediation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_event_delivery_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "notify",
                        principalTable: "delivery",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_org_created",
                schema: "notify",
                table: "delivery",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_digest",
                schema: "notify",
                table: "delivery",
                columns: new[] { "rule_target_id", "recipient_key", "period_end" },
                unique: true,
                filter: "kind = 'digest'");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_waiting",
                schema: "notify",
                table: "delivery",
                columns: new[] { "organization_id", "recipient_key", "channel" },
                unique: true,
                filter: "kind = 'immediate' AND state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_event_delivery",
                schema: "notify",
                table: "delivery_event",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_event",
                schema: "notify",
                table: "delivery_event",
                columns: new[] { "organization_id", "recipient_key", "channel", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_digest_item_period",
                schema: "notify",
                table: "digest_item",
                columns: new[] { "rule_target_id", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ux_digest_item",
                schema: "notify",
                table: "digest_item",
                columns: new[] { "rule_target_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_org",
                schema: "notify",
                table: "endpoint",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ux_external_address",
                schema: "notify",
                table: "external_address",
                columns: new[] { "organization_id", "normalized_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_user",
                schema: "notify",
                table: "notification",
                columns: new[] { "organization_id", "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_event",
                schema: "notify",
                table: "notification",
                columns: new[] { "user_id", "organization_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_event_org",
                schema: "notify",
                table: "pending_event",
                columns: new[] { "organization_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_org",
                schema: "notify",
                table: "rule",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_rule_target_due",
                schema: "notify",
                table: "rule_target",
                column: "next_digest_at",
                filter: "mode = 'digest'");

            migrationBuilder.CreateIndex(
                name: "ix_rule_target_rule",
                schema: "notify",
                table: "rule_target",
                column: "rule_id");

            migrationBuilder.Sql("""
                ALTER TABLE notify.rule ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.rule
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.rule_target ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.rule_target
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.endpoint ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.endpoint
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.external_address ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.external_address
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.recipient_state ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.recipient_state
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.delivery ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.delivery
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.delivery_event ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.delivery_event
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.digest_item ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.digest_item
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE notify.pending_event ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON notify.pending_event
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                ALTER TABLE notify.notification ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own_read ON notify.notification FOR SELECT
                    USING (organization_id = platform.current_organization_id() AND user_id = platform.current_user_id());
                CREATE POLICY own_update ON notify.notification FOR UPDATE
                    USING (organization_id = platform.current_organization_id() AND user_id = platform.current_user_id())
                    WITH CHECK (organization_id = platform.current_organization_id() AND user_id = platform.current_user_id());
                CREATE POLICY deliver ON notify.notification FOR INSERT
                    WITH CHECK (organization_id = platform.current_organization_id()
                        AND user_id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()));

                ALTER TABLE notify.user_preference ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own_read ON notify.user_preference FOR SELECT
                    USING (organization_id = platform.current_organization_id()
                        AND (user_id = platform.current_user_id() OR platform.current_user_id() IS NULL));
                CREATE POLICY own_write ON notify.user_preference FOR ALL
                    USING (organization_id = platform.current_organization_id() AND user_id = platform.current_user_id())
                    WITH CHECK (organization_id = platform.current_organization_id() AND user_id = platform.current_user_id());

                CREATE FUNCTION notify.signal_organization_changed() RETURNS trigger
                    LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        PERFORM pg_notify('ardea_org_changed', NEW.organization_id::text);
                        RETURN NULL;
                    END
                    $$;
                CREATE TRIGGER signal_organization_changed AFTER INSERT OR UPDATE ON notify.notification
                    FOR EACH ROW EXECUTE FUNCTION notify.signal_organization_changed();

                CREATE FUNCTION notify.organizations_with_warning_rules()
                    RETURNS TABLE (organization_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT DISTINCT r.organization_id FROM notify.rule r
                        WHERE r.enabled AND 'warning.published' = ANY (r.events) AND cardinality(r.project_ids) = 0
                    $$;

                CREATE FUNCTION notify.due_digest_targets(p_now timestamptz)
                    RETURNS TABLE (organization_id uuid, target_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT t.organization_id, t.id FROM notify.rule_target t
                        WHERE t.mode = 'digest' AND (t.next_digest_at IS NULL OR t.next_digest_at <= p_now)
                    $$;

                WITH orgs AS (SELECT id FROM iam.organization),
                critical AS (
                    INSERT INTO notify.rule (organization_id, name, enabled, project_ids, min_level, events, preset, created_at, changed_at)
                    SELECT id, 'Critical at once', true, '{}', 5,
                        ARRAY['action.raised', 'action.level_raised', 'action.exploitation_escalated', 'action.level_lowered', 'action.withdrawn', 'backfill.completed'],
                        'critical', now(), now() FROM orgs
                    RETURNING id, organization_id),
                high AS (
                    INSERT INTO notify.rule (organization_id, name, enabled, project_ids, min_level, events, preset, created_at, changed_at)
                    SELECT id, 'High', true, '{}', 4,
                        ARRAY['action.raised', 'action.level_raised', 'action.exploitation_escalated', 'action.level_lowered', 'action.withdrawn', 'backfill.completed'],
                        'high', now(), now() FROM orgs
                    RETURNING id, organization_id),
                weekly AS (
                    INSERT INTO notify.rule (organization_id, name, enabled, project_ids, min_level, events, preset, created_at, changed_at)
                    SELECT id, 'Weekly summary', true, '{}', 2,
                        ARRAY['action.raised', 'action.level_raised', 'action.exploitation_escalated', 'action.level_lowered', 'action.withdrawn', 'backfill.completed', 'warning.published'],
                        'weekly', now(), now() FROM orgs
                    RETURNING id, organization_id)
                INSERT INTO notify.rule_target (organization_id, rule_id, channel, recipient_kind, recipient_ref, mode, digest_cron, send_empty, created_at)
                SELECT organization_id, id, 'in_app', 'role', 'admin', 'immediate', NULL, false, now() FROM critical
                UNION ALL SELECT organization_id, id, 'email', 'role', 'admin', 'immediate', NULL, false, now() FROM critical
                UNION ALL SELECT organization_id, id, 'in_app', 'role', 'viewer', 'immediate', NULL, false, now() FROM high
                UNION ALL SELECT organization_id, id, 'email', 'role', 'viewer', 'digest', '0 8 * * 1-5', false, now() FROM high
                UNION ALL SELECT organization_id, id, 'email', 'role', 'viewer', 'digest', '0 8 * * 1', true, now() FROM weekly;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION notify.due_digest_targets(timestamptz);
                DROP FUNCTION notify.organizations_with_warning_rules();
                DROP TRIGGER signal_organization_changed ON notify.notification;
                DROP FUNCTION notify.signal_organization_changed();
                """);

            migrationBuilder.DropTable(
                name: "delivery_event",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "digest_item",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "endpoint",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "external_address",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "pending_event",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "recipient_state",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "user_preference",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "delivery",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "rule_target",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "rule",
                schema: "notify");
        }
    }
}
