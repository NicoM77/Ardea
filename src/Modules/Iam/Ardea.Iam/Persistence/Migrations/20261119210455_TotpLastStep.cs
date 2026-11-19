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
            // The time step of the last accepted authenticator code (issue #85, ADR 045). The row-level policy "own"
            // on iam."user" covers the new column: an account writes only its own row (ADR 029).
            migrationBuilder.AddColumn<long>(
                name: "totp_last_step",
                schema: "iam",
                table: "user",
                type: "bigint",
                nullable: true);

            // Defence in depth: whatever writes the column, the used step never goes back and never becomes empty
            // again, so a used code cannot become valid again. Not SECURITY DEFINER; it runs as the writer.
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
