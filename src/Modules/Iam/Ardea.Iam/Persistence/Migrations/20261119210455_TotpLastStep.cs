using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TotpLastStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "iam",
                table: "user",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION iam.keep_totp_last_step() RETURNS trigger
                    LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        NEW.totp_last_step := GREATEST(OLD.totp_last_step, NEW.totp_last_step);
                        RETURN NEW;
                    END
                    $$;
                CREATE TRIGGER keep_totp_last_step BEFORE UPDATE OF totp_last_step ON iam."user"
                    FOR EACH ROW WHEN (NEW.totp_last_step IS DISTINCT FROM OLD.totp_last_step)
                    EXECUTE FUNCTION iam.keep_totp_last_step();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER keep_totp_last_step ON iam."user";
                DROP FUNCTION iam.keep_totp_last_step();
                """);
            migrationBuilder.DropColumn(
                name: "totp_last_step",
                schema: "iam",
                table: "user");
        }
    }
}
