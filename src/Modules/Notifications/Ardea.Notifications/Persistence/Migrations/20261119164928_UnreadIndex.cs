using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnreadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_notification_unread",
                schema: "notify",
                table: "notification",
                columns: new[] { "organization_id", "user_id", "created_at", "id" },
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_unread",
                schema: "notify",
                table: "notification");
        }
    }
}
