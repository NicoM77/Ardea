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
