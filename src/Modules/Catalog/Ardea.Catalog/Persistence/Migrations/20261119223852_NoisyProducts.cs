using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Catalog.Persistence.Migrations
{
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
