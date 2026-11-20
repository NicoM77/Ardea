using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatusFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION assess.model_usage_since(p_since timestamptz)
                    RETURNS TABLE (provider text, calls bigint, tokens_in bigint, tokens_out bigint, refused bigint)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT a.provider::text, sum(a.attempts), sum(a.tokens_in), sum(a.tokens_out), count(*) FILTER (WHERE a.engine = 'rules')
                        FROM assess.assessment a
                        WHERE a.provider IS NOT NULL AND a.created_at >= p_since
                        GROUP BY a.provider
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION assess.model_usage_since(timestamptz);
                """);
        }
    }
}
