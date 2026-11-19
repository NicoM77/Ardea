using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <summary>
    /// Task 5a.2 (master plan 17.5, ADR 046): a reminder has no assignee, so the event <c>action.assigned</c> is gone. Rules
    /// no longer name it, and the in-app notifications «Assigned to you» leave the inbox. Waiting digest items of the
    /// event stay: Assessment no longer hands out such events, and a digest leaves out what it cannot read.
    /// </summary>
    public partial class WithoutAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- A rule that named nothing else would match nothing and could not be saved again: it goes, with its
                -- targets (their deliveries keep their history, their waiting digest items go with them).
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
