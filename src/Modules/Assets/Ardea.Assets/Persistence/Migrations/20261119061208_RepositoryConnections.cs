using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_connection",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    host_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    token_enc = table.Column<string>(type: "text", nullable: true),
                    token_hint = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    token_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_ok_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_connection", x => x.id);
                    table.CheckConstraint("ck_repository_connection_kind", "token_kind IN ('none', 'fine_grained', 'classic', 'other')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_repository_connection_org",
                schema: "assets",
                table: "repository_connection",
                column: "organization_id");

            migrationBuilder.Sql("""
                ALTER TABLE assets.repository_connection ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.repository_connection
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_connection",
                schema: "assets");
        }
    }
}
