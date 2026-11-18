using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ardea.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subject_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_key",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_key", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    coalesce_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                    table.CheckConstraint("ck_job_state", "state IN ('pending', 'running', 'succeeded', 'failed', 'dead')");
                });

            migrationBuilder.CreateTable(
                name: "schedule",
                schema: "platform",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    job_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "platform",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    secret_value = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "worker_heartbeat",
                schema: "platform",
                columns: table => new
                {
                    worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    beat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_heartbeat", x => x.worker_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_org_at",
                schema: "platform",
                table: "audit_event",
                columns: new[] { "organization_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_due",
                schema: "platform",
                table: "job",
                columns: new[] { "priority", "run_at" },
                filter: "state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_job_finished",
                schema: "platform",
                table: "job",
                column: "finished_at",
                filter: "state IN ('succeeded', 'dead')");

            migrationBuilder.CreateIndex(
                name: "ix_job_running_lease",
                schema: "platform",
                table: "job",
                column: "lease_until",
                filter: "state = 'running'");

            migrationBuilder.CreateIndex(
                name: "ux_job_pending_coalesce_key",
                schema: "platform",
                table: "job",
                column: "coalesce_key",
                unique: true,
                filter: "state = 'pending' AND coalesce_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "data_protection_key",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "job",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "schedule",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "worker_heartbeat",
                schema: "platform");
        }
    }
}
