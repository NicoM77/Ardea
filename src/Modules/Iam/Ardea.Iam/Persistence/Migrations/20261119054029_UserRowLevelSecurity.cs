using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // User data (master plan 4.1, 9.2; ADR 029): a user reads and writes their own rows. Members
            // of the transaction's organisation see each other's account row (the member directory,
            // name and address are all the code reads), but nobody writes another account.
            migrationBuilder.Sql("""
                ALTER TABLE iam."user" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON iam."user"
                    USING (id = platform.current_user_id()
                        OR id IN (SELECT m.user_id FROM iam.membership m WHERE m.organization_id = platform.current_organization_id()))
                    WITH CHECK (id = platform.current_user_id());

                ALTER TABLE iam.user_claim ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON iam.user_claim USING (user_id = platform.current_user_id()) WITH CHECK (user_id = platform.current_user_id());
                ALTER TABLE iam.user_login ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON iam.user_login USING (user_id = platform.current_user_id()) WITH CHECK (user_id = platform.current_user_id());
                ALTER TABLE iam.user_token ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON iam.user_token USING (user_id = platform.current_user_id()) WITH CHECK (user_id = platform.current_user_id());
                ALTER TABLE iam.session ENABLE ROW LEVEL SECURITY;
                CREATE POLICY own ON iam.session USING (user_id = platform.current_user_id()) WITH CHECK (user_id = platform.current_user_id());

                -- System operation "sign-in": the account of an address, before a user context exists.
                CREATE FUNCTION iam.user_id_by_email(p_normalized_email text) RETURNS uuid
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$ SELECT u.id FROM iam."user" u WHERE u.normalized_email = p_normalized_email $$;

                -- Instance figure for the setup: whether any account exists yet.
                CREATE FUNCTION iam.any_user() RETURNS boolean
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$ SELECT EXISTS (SELECT 1 FROM iam."user") $$;

                -- The session check in front of every request: valid, not revoked, within both
                -- lifetimes, security stamp unchanged; marks the session as seen at most every five minutes.
                CREATE FUNCTION iam.validate_session(p_session uuid, p_user uuid, p_stamp text, p_idle interval) RETURNS boolean
                    LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        WITH valid AS (
                            SELECT s.id FROM iam.session s JOIN iam."user" u ON u.id = s.user_id
                            WHERE s.id = p_session AND s.user_id = p_user AND s.revoked_at IS NULL AND s.expires_at > now()
                              AND s.last_seen_at > now() - p_idle AND u.security_stamp IS NOT DISTINCT FROM p_stamp
                        ), touched AS (
                            UPDATE iam.session SET last_seen_at = now()
                            WHERE id IN (SELECT id FROM valid) AND last_seen_at < now() - interval '5 minutes'
                            RETURNING id
                        )
                        SELECT count(*) = 1 FROM valid
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION iam.validate_session(uuid, uuid, text, interval);
                DROP FUNCTION iam.any_user();
                DROP FUNCTION iam.user_id_by_email(text);
                DROP POLICY own ON iam.session;
                ALTER TABLE iam.session DISABLE ROW LEVEL SECURITY;
                DROP POLICY own ON iam.user_token;
                ALTER TABLE iam.user_token DISABLE ROW LEVEL SECURITY;
                DROP POLICY own ON iam.user_login;
                ALTER TABLE iam.user_login DISABLE ROW LEVEL SECURITY;
                DROP POLICY own ON iam.user_claim;
                ALTER TABLE iam.user_claim DISABLE ROW LEVEL SECURITY;
                DROP POLICY own ON iam."user";
                ALTER TABLE iam."user" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
