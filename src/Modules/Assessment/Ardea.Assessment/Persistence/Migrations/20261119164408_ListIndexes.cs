using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding");

            migrationBuilder.CreateIndex(
                name: "ix_remediation_level",
                schema: "assess",
                table: "remediation",
                columns: new[] { "organization_id", "level", "number" });

            migrationBuilder.CreateIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding",
                columns: new[] { "organization_id", "level", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_alert_list",
                schema: "assess",
                table: "alert",
                columns: new[] { "organization_id", "level", "number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_remediation_level",
                schema: "assess",
                table: "remediation");

            migrationBuilder.DropIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding");

            migrationBuilder.DropIndex(
                name: "ix_alert_list",
                schema: "assess",
                table: "alert");

            migrationBuilder.CreateIndex(
                name: "ix_finding_org_level",
                schema: "assess",
                table: "finding",
                columns: new[] { "organization_id", "level" });
        }
    }
}
