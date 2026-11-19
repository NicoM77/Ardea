using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatusFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Instance status (master plan 12.7; task 6.6): deliveries by state since a point in time, over all
                -- organisations. Counts only; the status runs without an organisation.
                CREATE FUNCTION notify.delivery_counts_since(p_since timestamptz)
                    RETURNS TABLE (state text, deliveries bigint)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT d.state::text, count(*) FROM notify.delivery d
                        WHERE d.created_at >= p_since OR d.state IN ('pending', 'sending', 'failed')
                        GROUP BY d.state
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION notify.delivery_counts_since(timestamptz);
                """);
        }
    }
}
