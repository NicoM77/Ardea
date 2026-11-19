using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "current_assessment_id",
                schema: "assess",
                table: "alert",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assessment",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_revision = table.Column<int>(type: "integer", nullable: false),
                    input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    engine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ruleset_version = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    output = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    remediation_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment", x => x.id);
                    table.CheckConstraint("ck_assessment_counts", "attempts BETWEEN 1 AND 2 AND tokens_in >= 0 AND tokens_out >= 0 AND duration_ms >= 0");
                    table.CheckConstraint("ck_assessment_engine", "engine IN ('rules', 'rules+model')");
                    table.ForeignKey(
                        name: "fk_assessment_alert_alert_id",
                        column: x => x.alert_id,
                        principalSchema: "assess",
                        principalTable: "alert",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_alert",
                schema: "assess",
                table: "assessment",
                column: "alert_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_org_created",
                schema: "assess",
                table: "assessment",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.Sql("""
                ALTER TABLE assess.assessment ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.assessment
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                -- An assessment is immutable (master plan 4.6): a correction is a new row, never a change.
                CREATE FUNCTION assess.assessment_immutable() RETURNS trigger
                    LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp
                    AS $$
                    BEGIN
                        RAISE EXCEPTION 'assess.assessment is immutable' USING ERRCODE = 'restrict_violation';
                    END
                    $$;
                CREATE TRIGGER assessment_immutable BEFORE UPDATE ON assess.assessment
                    FOR EACH ROW EXECUTE FUNCTION assess.assessment_immutable();

                -- System operation "token quota of the instance" (master plan 7.8, 9.2, ADR 037): whether all
                -- organisations together used up a quota since a point in time. Returns a flag only.
                CREATE FUNCTION assess.model_tokens_exhausted(since timestamptz, quota bigint)
                    RETURNS boolean
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT coalesce(sum(a.tokens_in + a.tokens_out), 0) >= quota FROM assess.assessment a WHERE a.created_at >= since
                    $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION assess.model_tokens_exhausted(timestamptz, bigint);
                DROP TRIGGER assessment_immutable ON assess.assessment;
                DROP FUNCTION assess.assessment_immutable();
                """);

            migrationBuilder.DropTable(
                name: "assessment",
                schema: "assess");

            migrationBuilder.DropColumn(
                name: "current_assessment_id",
                schema: "assess",
                table: "alert");
        }
    }
}
