using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Entries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_entry",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    primary_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    material_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remediation_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cvss_score = table.Column<double>(type: "double precision", nullable: true),
                    cvss_version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cvss_vector = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    exploitation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    exploitation_evidence = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    epss_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    fix_available = table.Column<bool>(type: "boolean", nullable: false),
                    cwe_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    authority_warning = table.Column<bool>(type: "boolean", nullable: false),
                    provenance = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_entry", x => x.id);
                    table.CheckConstraint("ck_security_entry_exploitation", "exploitation IN ('none', 'poc', 'active')");
                    table.CheckConstraint("ck_security_entry_kind", "kind IN ('vulnerability', 'warning')");
                    table.CheckConstraint("ck_security_entry_state", "state IN ('published', 'rejected', 'disputed', 'merged')");
                });

            migrationBuilder.CreateTable(
                name: "entry_affected",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    product = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    product_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cpe_vendor = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cpe_product = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    purl_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    purl_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version_scheme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    introduced = table.Column<string>(type: "text", nullable: true),
                    @fixed = table.Column<string>(name: "fixed", type: "text", nullable: true),
                    last_affected = table.Column<string>(type: "text", nullable: true),
                    exact = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_affected", x => x.id);
                    table.CheckConstraint("ck_entry_affected_default_status", "default_status IN ('affected', 'unaffected', 'unknown')");
                    table.CheckConstraint("ck_entry_affected_status", "status IN ('affected', 'unaffected', 'unknown')");
                    table.ForeignKey(
                        name: "fk_entry_affected_security_entry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "intel",
                        principalTable: "security_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entry_alias",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_alias", x => x.id);
                    table.ForeignKey(
                        name: "fk_entry_alias_security_entry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "intel",
                        principalTable: "security_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entry_change",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_kinds = table.Column<string[]>(type: "text[]", nullable: false),
                    is_material = table.Column<bool>(type: "boolean", nullable: false),
                    diff = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    triggered_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_entry_change_security_entry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "intel",
                        principalTable: "security_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entry_reference",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_reference", x => x.id);
                    table.ForeignKey(
                        name: "fk_entry_reference_security_entry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "intel",
                        principalTable: "security_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entry_source",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_source", x => x.id);
                    table.CheckConstraint("ck_entry_source_relation", "relation IN ('describes', 'mentions', 'signal')");
                    table.ForeignKey(
                        name: "fk_entry_source_security_entry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "intel",
                        principalTable: "security_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entry_affected_cpe",
                schema: "intel",
                table: "entry_affected",
                columns: new[] { "cpe_vendor", "cpe_product" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_affected_entry",
                schema: "intel",
                table: "entry_affected",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_entry_affected_product_key",
                schema: "intel",
                table: "entry_affected",
                column: "product_key");

            migrationBuilder.CreateIndex(
                name: "ix_entry_affected_purl",
                schema: "intel",
                table: "entry_affected",
                columns: new[] { "purl_type", "purl_name" });

            migrationBuilder.CreateIndex(
                name: "ix_entry_alias_entry",
                schema: "intel",
                table: "entry_alias",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_entry_alias",
                schema: "intel",
                table: "entry_alias",
                columns: new[] { "scheme", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_entry_change_revision",
                schema: "intel",
                table: "entry_change",
                columns: new[] { "entry_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_reference_entry",
                schema: "intel",
                table: "entry_reference",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_entry_source_entry",
                schema: "intel",
                table: "entry_source",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_security_entry_changed_at",
                schema: "intel",
                table: "security_entry",
                column: "changed_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_security_entry_primary_key",
                schema: "intel",
                table: "security_entry",
                column: "primary_key",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE intel.security_entry ADD COLUMN search tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('simple', coalesce(primary_key, '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(title, '')), 'B') ||
                    setweight(to_tsvector('english', coalesce(summary, '') || ' ' || coalesce(description, '')), 'C')) STORED;
                CREATE INDEX ix_security_entry_search ON intel.security_entry USING gin (search);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entry_affected",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "entry_alias",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "entry_change",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "entry_reference",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "entry_source",
                schema: "intel");

            migrationBuilder.DropTable(
                name: "security_entry",
                schema: "intel");
        }
    }
}
