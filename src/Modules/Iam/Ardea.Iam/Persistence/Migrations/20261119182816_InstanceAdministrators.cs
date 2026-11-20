using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstanceAdministrators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION iam.instance_administrators() RETURNS SETOF uuid
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$ SELECT u.id FROM iam."user" u WHERE u.is_instance_admin ORDER BY u.id $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION iam.instance_administrators();");
        }
    }
}
