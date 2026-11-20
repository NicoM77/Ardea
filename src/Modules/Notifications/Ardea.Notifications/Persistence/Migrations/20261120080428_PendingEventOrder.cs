using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingEventOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_event_org",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.AddColumn<long>(
                name: "seq",
                schema: "notify",
                table: "pending_event",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateIndex(
                name: "ix_pending_event_org",
                schema: "notify",
                table: "pending_event",
                columns: new[] { "organization_id", "at", "seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_event_org",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.DropColumn(
                name: "seq",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.CreateIndex(
                name: "ix_pending_event_org",
                schema: "notify",
                table: "pending_event",
                columns: new[] { "organization_id", "at" });
        }
    }
}
