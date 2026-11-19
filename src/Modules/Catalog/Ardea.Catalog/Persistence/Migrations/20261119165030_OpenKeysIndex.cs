using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpenKeysIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_unknown_product_outcome_count",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.CreateIndex(
                name: "ix_unknown_product_outcome_count",
                schema: "catalog",
                table: "unknown_product",
                columns: new[] { "outcome", "in_kev", "record_count", "raw_key" },
                descending: new[] { false, true, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_unknown_product_outcome_count",
                schema: "catalog",
                table: "unknown_product");

            migrationBuilder.CreateIndex(
                name: "ix_unknown_product_outcome_count",
                schema: "catalog",
                table: "unknown_product",
                columns: new[] { "outcome", "record_count" });
        }
    }
}
