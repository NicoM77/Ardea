using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "project",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    criticality = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    exposure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                    table.CheckConstraint("ck_project_criticality", "criticality IN ('low', 'normal', 'high')");
                    table.CheckConstraint("ck_project_exposure", "exposure IN ('internet', 'internal', 'isolated')");
                });

            migrationBuilder.CreateTable(
                name: "component",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    custom_vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    custom_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version_scheme = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    packaging = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exposure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    criticality = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    protection = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    protection_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    repository_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dependency_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    dependency_relation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    evidence = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_component", x => x.id);
                    table.CheckConstraint("ck_component_criticality", "criticality IS NULL OR criticality IN ('low', 'normal', 'high')");
                    table.CheckConstraint("ck_component_exposure", "exposure IS NULL OR exposure IN ('internet', 'internal', 'isolated')");
                    table.CheckConstraint("ck_component_kind", "kind IN ('os', 'runtime', 'framework', 'cms', 'database', 'messaging', 'container', 'network', 'virtualization', 'library', 'plugin', 'service', 'other')");
                    table.CheckConstraint("ck_component_origin", "origin IN ('manual', 'repository')");
                    table.CheckConstraint("ck_component_packaging", "packaging IN ('upstream', 'distribution', 'unknown')");
                    table.CheckConstraint("ck_component_product", "product_key IS NOT NULL OR custom_name IS NOT NULL");
                    table.CheckConstraint("ck_component_protection", "protection IN ('none', 'effective')");
                    table.CheckConstraint("ck_component_relation", "dependency_relation IS NULL OR dependency_relation IN ('direct', 'transitive', 'unknown')");
                    table.CheckConstraint("ck_component_scope", "dependency_scope IS NULL OR dependency_scope IN ('runtime', 'development', 'unknown')");
                    table.ForeignKey(
                        name: "fk_component_component_parent_component_id",
                        column: x => x.parent_component_id,
                        principalSchema: "assets",
                        principalTable: "component",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_component_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "assets",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_component_org_product",
                schema: "assets",
                table: "component",
                columns: new[] { "organization_id", "product_key" });

            migrationBuilder.CreateIndex(
                name: "ix_component_parent_component_id",
                schema: "assets",
                table: "component",
                column: "parent_component_id");

            migrationBuilder.CreateIndex(
                name: "ix_component_project",
                schema: "assets",
                table: "component",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_org_name",
                schema: "assets",
                table: "project",
                columns: new[] { "organization_id", "name" });

            // Organisation data: row-level security as the third layer (master plan 9.2, ADR 029).
            migrationBuilder.Sql("""
                ALTER TABLE assets.project ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.project
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assets.component ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.component
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "component",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "project",
                schema: "assets");
        }
    }
}
