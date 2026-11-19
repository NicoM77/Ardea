using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixVersionsInText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "fix_versions_in_text",
                schema: "intel",
                table: "security_entry",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fix_versions_in_text",
                schema: "intel",
                table: "security_entry");
        }
    }
}
