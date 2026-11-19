using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tenant of the current transaction, as the unit of work sets it (ADR 029). Empty or
            // unset means none: then no organisation's or user's rows are visible.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.current_organization_id() RETURNS uuid
                    LANGUAGE sql STABLE PARALLEL SAFE
                    AS $$ SELECT nullif(current_setting('ardea.org_id', true), '')::uuid $$;
                CREATE FUNCTION platform.current_user_id() RETURNS uuid
                    LANGUAGE sql STABLE PARALLEL SAFE
                    AS $$ SELECT nullif(current_setting('ardea.user_id', true), '')::uuid $$;

                ALTER TABLE platform.audit_event ENABLE ROW LEVEL SECURITY;
                -- An organisation reads its own events; instance events (no organisation) are written
                -- from any unit of work but read only by the operator.
                CREATE POLICY tenant_read ON platform.audit_event FOR SELECT
                    USING (organization_id = platform.current_organization_id());
                CREATE POLICY tenant_write ON platform.audit_event FOR INSERT
                    WITH CHECK (organization_id IS NULL OR organization_id = platform.current_organization_id());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_write ON platform.audit_event;
                DROP POLICY tenant_read ON platform.audit_event;
                ALTER TABLE platform.audit_event DISABLE ROW LEVEL SECURITY;
                DROP FUNCTION platform.current_user_id();
                DROP FUNCTION platform.current_organization_id();
                """);
        }
    }
}
