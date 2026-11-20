using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    public partial class WithoutAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM notify.rule WHERE events <@ ARRAY['action.assigned']::text[] AND cardinality(events) > 0;
                UPDATE notify.rule SET events = array_remove(events, 'action.assigned') WHERE 'action.assigned' = ANY (events);
                DELETE FROM notify.notification WHERE kind = 'action.assigned';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
