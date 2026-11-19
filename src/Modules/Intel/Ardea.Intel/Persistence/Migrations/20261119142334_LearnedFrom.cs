using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LearnedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "learned_from",
                schema: "intel",
                table: "entry_affected",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entry_affected_learned_from",
                schema: "intel",
                table: "entry_affected",
                column: "learned_from",
                filter: "learned_from IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entry_affected_learned_from",
                schema: "intel",
                table: "entry_affected");

            migrationBuilder.DropColumn(
                name: "learned_from",
                schema: "intel",
                table: "entry_affected");
        }
    }
}
