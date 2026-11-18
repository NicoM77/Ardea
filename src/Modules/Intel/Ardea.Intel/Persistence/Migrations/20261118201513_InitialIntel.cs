using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "intel");

            migrationBuilder.CreateTable(
                name: "source",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(200)", maxLength: 200, nullable: false),
                    operator_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adapter = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    config = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    attribution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    terms_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parser_version = table.Column<int>(type: "integer", nullable: false),
                    next_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cursor = table.Column<string>(type: "text", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    health = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    health_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source", x => x.id);
                    table.CheckConstraint("ck_source_health", "health IN ('healthy', 'degraded', 'failing', 'disabled')");
                    table.CheckConstraint("ck_source_kind", "kind IN ('record', 'advisory', 'signal')");
                    table.CheckConstraint("ck_source_operator_kind", "operator_kind IN ('authority', 'mandated', 'recognized', 'vendor', 'community')");
                    table.CheckConstraint("ck_source_terms_mode", "terms_mode IN ('full', 'metadata_only')");
                });

            migrationBuilder.CreateTable(
                name: "source_record",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    source_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    parse_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_record", x => x.id);
                    table.CheckConstraint("ck_source_record_parse_status", "parse_status IN ('pending', 'parsed', 'quarantined')");
                    table.ForeignKey(
                        name: "fk_source_record_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "intel",
                        principalTable: "source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_run",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fetched = table.Column<int>(type: "integer", nullable: false),
                    changed = table.Column<int>(type: "integer", nullable: false),
                    quarantined = table.Column<int>(type: "integer", nullable: false),
                    checkpoints = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_source_run_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "intel",
                        principalTable: "source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_record_revision",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    source_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_record_revision", x => x.id);
                    table.ForeignKey(
                        name: "fk_source_record_revision_source_record_source_record_id",
                        column: x => x.source_record_id,
                        principalSchema: "intel",
                        principalTable: "source_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_next_sync_at",
                schema: "intel",
                table: "source",
                column: "next_sync_at",
                filter: "enabled");

            migrationBuilder.CreateIndex(
                name: "ux_source_key",
                schema: "intel",
                table: "source",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_record_pending",
                schema: "intel",
                table: "source_record",
                columns: new[] { "source_id", "parse_status" },
                filter: "parse_status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ux_source_record_external_id",
                schema: "intel",
                table: "source_record",
                columns: new[] { "source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_source_record_revision",
                schema: "intel",
                table: "source_record_revision",
                columns: new[] { "source_record_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_run_source_started",
                schema: "intel",
                table: "source_run",
                columns: new[] { "source_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_record_revision",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "source_run",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "source_record",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "source",
                schema: "intel");
        }
    }
}
