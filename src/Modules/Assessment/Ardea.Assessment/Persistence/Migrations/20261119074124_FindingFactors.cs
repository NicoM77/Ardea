using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FindingFactors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "factors",
                schema: "assess",
                table: "finding",
                type: "jsonb",
                nullable: false,
                // Findings from before the rule set carry no explanation until their next assessment.
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "factors",
                schema: "assess",
                table: "finding");
        }
    }
}
