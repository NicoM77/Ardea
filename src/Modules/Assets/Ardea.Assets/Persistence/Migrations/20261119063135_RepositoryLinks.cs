using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_link",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "character varying(250)", maxLength: 250, nullable: true),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    head_etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_sbom = table.Column<byte[]>(type: "bytea", nullable: true),
                    pending_shrink = table.Column<byte[]>(type: "bytea", nullable: true),
                    pending_shrink_commit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    unavailable_raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_link", x => x.id);
                    table.CheckConstraint("ck_repository_link_state", "state IN ('pending', 'ok', 'partial', 'failing')");
                    table.ForeignKey(
                        name: "fk_repository_link_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "assets",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repository_link_repository_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "assets",
                        principalTable: "repository_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_suggestion",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_component_suggestion", x => x.id);
                    table.CheckConstraint("ck_component_suggestion_state", "state IN ('open', 'accepted', 'dismissed')");
                    table.ForeignKey(
                        name: "fk_component_suggestion_component_component_id",
                        column: x => x.component_id,
                        principalSchema: "assets",
                        principalTable: "component",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_component_suggestion_repository_link_repository_link_id",
                        column: x => x.repository_link_id,
                        principalSchema: "assets",
                        principalTable: "repository_link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_component_repository_link_id",
                schema: "assets",
                table: "component",
                column: "repository_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_component_suggestion_component_id",
                schema: "assets",
                table: "component_suggestion",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_component_suggestion_project",
                schema: "assets",
                table: "component_suggestion",
                columns: new[] { "project_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_component_suggestion",
                schema: "assets",
                table: "component_suggestion",
                columns: new[] { "repository_link_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repository_link_connection_id",
                schema: "assets",
                table: "repository_link",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_repository_link_due",
                schema: "assets",
                table: "repository_link",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "ux_repository_link",
                schema: "assets",
                table: "repository_link",
                columns: new[] { "project_id", "connection_id", "owner", "name", "path" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "fk_component_repository_link_repository_link_id",
                schema: "assets",
                table: "component",
                column: "repository_link_id",
                principalSchema: "assets",
                principalTable: "repository_link",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                ALTER TABLE assets.repository_link ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.repository_link
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                ALTER TABLE assets.component_suggestion ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.component_suggestion
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                -- System operation "read repositories" (master plan 9.2): which links are due now. Claims
                -- them by moving their due time past the reading's time limit and returns ids only; the
                -- reading itself runs as a job in the link's organisation.
                CREATE FUNCTION assets.claim_due_repository_links(p_limit integer, p_hold interval)
                    RETURNS TABLE (organization_id uuid, link_id uuid)
                    LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        UPDATE assets.repository_link l SET next_check_at = now() + p_hold
                        WHERE l.id IN (
                            SELECT d.id FROM assets.repository_link d
                            WHERE d.next_check_at <= now()
                            ORDER BY d.next_check_at
                            LIMIT p_limit
                            FOR UPDATE SKIP LOCKED)
                        RETURNING l.organization_id, l.id
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION assets.claim_due_repository_links(integer, interval);");

            migrationBuilder.DropForeignKey(
                name: "fk_component_repository_link_repository_link_id",
                schema: "assets",
                table: "component");

            migrationBuilder.DropTable(
                name: "component_suggestion",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "repository_link",
                schema: "assets");

            migrationBuilder.DropIndex(
                name: "ix_component_repository_link_id",
                schema: "assets",
                table: "component");
        }
    }
}
