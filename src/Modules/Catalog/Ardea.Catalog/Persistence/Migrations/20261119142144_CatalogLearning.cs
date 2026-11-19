using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_unknown_product_outcome",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.DropCheckConstraint(
                name: "ck_product_learned_by",
                schema: "catalog",
                table: "product");

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                schema: "catalog",
                table: "unknown_product",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "catalog",
                table: "unknown_product",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                schema: "catalog",
                table: "unknown_product",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposal",
                schema: "catalog",
                table: "unknown_product",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "learning_event",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    raw_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    product_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor = table.Column<Guid>(type: "uuid", nullable: true),
                    calls = table.Column<int>(type: "integer", nullable: false),
                    tokens = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_event", x => x.id);
                    table.CheckConstraint("ck_learning_event_kind", "kind IN ('collected', 'asked', 'learned', 'adopted', 'confirmed', 'rejected', 'withdrawn')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_unknown_product_outcome",
                schema: "catalog",
                table: "unknown_product",
                sql: "outcome IN ('open', 'pending', 'alias', 'new', 'unclear', 'rejected', 'withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_product_learned_by",
                schema: "catalog",
                table: "product",
                sql: "learned_by IS NULL OR learned_by IN ('rule', 'model', 'admin')");

            migrationBuilder.CreateIndex(
                name: "ix_learning_event_kind_at",
                schema: "catalog",
                table: "learning_event",
                columns: new[] { "kind", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_event",
                schema: "catalog");

            migrationBuilder.DropCheckConstraint(
                name: "ck_unknown_product_outcome",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.DropCheckConstraint(
                name: "ck_product_learned_by",
                schema: "catalog",
                table: "product");

            migrationBuilder.DropColumn(
                name: "attempts",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.DropColumn(
                name: "decided_by",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.DropColumn(
                name: "proposal",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.AddCheckConstraint(
                name: "ck_unknown_product_outcome",
                schema: "catalog",
                table: "unknown_product",
                sql: "outcome IN ('open', 'alias', 'new', 'unclear', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_product_learned_by",
                schema: "catalog",
                table: "product",
                sql: "learned_by IS NULL OR learned_by IN ('rule', 'model')");
        }
    }
}
