using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "product",
                schema: "catalog",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cpe_pairs = table.Column<string[]>(type: "text[]", nullable: false),
                    purls = table.Column<string[]>(type: "text[]", nullable: false),
                    aliases = table.Column<string>(type: "jsonb", nullable: false),
                    version_scheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    platform_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    related_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    image_names = table.Column<string[]>(type: "text[]", nullable: false),
                    origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    reasoning = table.Column<string>(type: "text", nullable: true),
                    evidence = table.Column<string>(type: "jsonb", nullable: true),
                    learned_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.key);
                    table.CheckConstraint("ck_product_confidence", "confidence IS NULL OR confidence IN ('high', 'medium', 'low')");
                    table.CheckConstraint("ck_product_curated_active", "origin <> 'curated' OR state = 'active'");
                    table.CheckConstraint("ck_product_kind", "kind IN ('os', 'runtime', 'framework', 'cms', 'database', 'messaging', 'container', 'network', 'virtualization', 'library', 'plugin', 'service', 'other')");
                    table.CheckConstraint("ck_product_learned_by", "learned_by IS NULL OR learned_by IN ('rule', 'model')");
                    table.CheckConstraint("ck_product_origin", "origin IN ('curated', 'learned')");
                    table.CheckConstraint("ck_product_state", "state IN ('active', 'pending', 'rejected', 'withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "unknown_product",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    raw_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    product = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpe_pairs = table.Column<string[]>(type: "text[]", nullable: false),
                    purl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    record_count = table.Column<int>(type: "integer", nullable: false),
                    in_kev = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "open"),
                    product_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unknown_product", x => x.id);
                    table.CheckConstraint("ck_unknown_product_outcome", "outcome IN ('open', 'alias', 'new', 'unclear', 'rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_origin_state",
                schema: "catalog",
                table: "product",
                columns: new[] { "origin", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_unknown_product_outcome_count",
                schema: "catalog",
                table: "unknown_product",
                columns: new[] { "outcome", "record_count" });

            migrationBuilder.CreateIndex(
                name: "ux_unknown_product_raw_key",
                schema: "catalog",
                table: "unknown_product",
                column: "raw_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "unknown_product",
                schema: "catalog");
        }
    }
}
