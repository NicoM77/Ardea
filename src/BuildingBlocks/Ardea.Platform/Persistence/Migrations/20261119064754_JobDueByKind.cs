using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobDueByKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_due",
                schema: "platform",
                table: "job");

            migrationBuilder.CreateIndex(
                name: "ix_job_due",
                schema: "platform",
                table: "job",
                columns: new[] { "kind", "priority", "run_at" },
                filter: "state = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_due",
                schema: "platform",
                table: "job");

            migrationBuilder.CreateIndex(
                name: "ix_job_due",
                schema: "platform",
                table: "job",
                columns: new[] { "priority", "run_at" },
                filter: "state = 'pending'");
        }
    }
}
