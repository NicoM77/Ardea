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
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.current_organization_id() RETURNS uuid
                    LANGUAGE sql STABLE PARALLEL SAFE
                    AS $$ SELECT nullif(current_setting('ardea.org_id', true), '')::uuid $$;
                CREATE FUNCTION platform.current_user_id() RETURNS uuid
                    LANGUAGE sql STABLE PARALLEL SAFE
                    AS $$ SELECT nullif(current_setting('ardea.user_id', true), '')::uuid $$;

                ALTER TABLE platform.audit_event ENABLE ROW LEVEL SECURITY;
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
