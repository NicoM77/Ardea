using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE iam.organization ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON iam.organization
                    USING (id = platform.current_organization_id()
                        OR id IN (SELECT m.organization_id FROM iam.membership m WHERE m.user_id = platform.current_user_id()))
                    WITH CHECK (id = platform.current_organization_id());

                ALTER TABLE iam.membership ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON iam.membership
                    USING (organization_id = platform.current_organization_id() OR user_id = platform.current_user_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                ALTER TABLE iam.invitation ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON iam.invitation
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                CREATE FUNCTION iam.invitation_organization(p_token_hash text, p_normalized_email text)
                    RETURNS uuid
                    LANGUAGE sql STABLE SECURITY DEFINER
                    SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT i.organization_id FROM iam.invitation i
                        WHERE i.token_hash = p_token_hash AND i.expires_at > now()
                          AND (p_normalized_email IS NULL OR i.normalized_email = p_normalized_email)
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION iam.invitation_organization(text, text);
                DROP POLICY tenant ON iam.invitation;
                ALTER TABLE iam.invitation DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant ON iam.membership;
                ALTER TABLE iam.membership DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant ON iam.organization;
                ALTER TABLE iam.organization DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
