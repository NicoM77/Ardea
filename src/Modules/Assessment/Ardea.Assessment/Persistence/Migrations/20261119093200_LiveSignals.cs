using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Live signals (master plan 10.2 «Live», task 3.11): every history row tells the API instances that
                -- the organisation changed. PostgreSQL sends a notification only when the transaction commits and
                -- sends equal notifications of one transaction once, so a job that writes many rows wakes a stream once.
                CREATE FUNCTION assess.signal_organization_changed() RETURNS trigger
                    LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        PERFORM pg_notify('ardea_org_changed', NEW.organization_id::text);
                        RETURN NULL;
                    END
                    $$;
                CREATE TRIGGER signal_organization_changed AFTER INSERT ON assess.assess_event
                    FOR EACH ROW EXECUTE FUNCTION assess.signal_organization_changed();

                -- System operation "recompute levels" (master plan 9.2, ADR 029; task 3.11): which organisation has
                -- an alert for which entry, for the command that queues their reassessment. Returns ids only.
                CREATE FUNCTION assess.alerts_to_reassess()
                    RETURNS TABLE (organization_id uuid, entry_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT a.organization_id, a.entry_id FROM assess.alert a
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION assess.alerts_to_reassess();
                DROP TRIGGER signal_organization_changed ON assess.assess_event;
                DROP FUNCTION assess.signal_organization_changed();
                """);
        }
    }
}
