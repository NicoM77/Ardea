using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Catalog.Persistence.Migrations
{
    /// <summary>
    /// Task 5a.3 (ADR 044 decision 3, ADR 047): products the curated catalogue marks as noisy raise «only important» notices
    /// only when exploited or warned about. A new column with a default, so the previous build keeps writing the catalogue.
    /// </summary>
    public partial class NoisyProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "noisy",
                schema: "catalog",
                table: "product",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "noisy",
                schema: "catalog",
                table: "product");
        }
    }
}
