using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComponentKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "component_key",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    key_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    strength = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_component_key", x => x.id);
                    table.CheckConstraint("ck_component_key_strength", "strength IN ('strong', 'weak')");
                    table.CheckConstraint("ck_component_key_type", "key_type IN ('cpe', 'product', 'purl')");
                    table.ForeignKey(
                        name: "fk_component_key_component_component_id",
                        column: x => x.component_id,
                        principalSchema: "assets",
                        principalTable: "component",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_component_key_value",
                schema: "assets",
                table: "component_key",
                columns: new[] { "key_type", "key_value" });

            migrationBuilder.CreateIndex(
                name: "ux_component_key",
                schema: "assets",
                table: "component_key",
                columns: new[] { "component_id", "key_type", "key_value" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE assets.component_key ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assets.component_key
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "component_key",
                schema: "assets");
        }
    }
}
