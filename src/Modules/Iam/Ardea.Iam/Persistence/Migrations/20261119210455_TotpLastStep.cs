using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TotpLastStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The time step of the last accepted authenticator code (issue #85). The row-level policy "own" on
            // iam."user" covers the new column: an account writes only its own row (ADR 029).
            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "iam",
                table: "user",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "iam",
                table: "user");
        }
    }
}
