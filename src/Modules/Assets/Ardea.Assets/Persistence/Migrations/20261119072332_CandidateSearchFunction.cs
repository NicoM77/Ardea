using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CandidateSearchFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- System operation "candidate search" (master plan 9.2, ADR 029): which organisations have a
                -- component under one of these keys. Returns organisation ids only; the assessment itself runs
                -- as a job in each organisation. Archived projects take no part in the matching.
                CREATE FUNCTION assets.organizations_with_component_keys(p_types text[], p_values text[])
                    RETURNS TABLE (organization_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT DISTINCT k.organization_id
                        FROM assets.component_key k
                        JOIN unnest(p_types, p_values) AS q(key_type, key_value)
                          ON k.key_type = q.key_type AND k.key_value = q.key_value
                        JOIN assets.project p ON p.id = k.project_id
                        WHERE p.archived_at IS NULL
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION assets.organizations_with_component_keys(text[], text[]);");
        }
    }
}
