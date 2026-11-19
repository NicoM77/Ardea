using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessment_feedback",
                schema: "assess",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verdict_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    verdict_before = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    level_before = table.Column<int>(type: "integer", nullable: false),
                    expected_level = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actor = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_feedback", x => x.id);
                    table.CheckConstraint("ck_assessment_feedback_correction", "kind = 'confirmed' OR ((verdict IS NOT NULL OR expected_level IS NOT NULL) AND reason IS NOT NULL)");
                    table.CheckConstraint("ck_assessment_feedback_kind", "kind IN ('confirmed', 'corrected')");
                    table.CheckConstraint("ck_assessment_feedback_levels", "level_before BETWEEN 1 AND 5 AND (expected_level IS NULL OR expected_level BETWEEN 1 AND 5)");
                    table.CheckConstraint("ck_assessment_feedback_source", "verdict_source IN ('rules', 'model', 'human')");
                    table.CheckConstraint("ck_assessment_feedback_verdicts", "verdict_before IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected') AND (verdict IS NULL OR verdict IN ('affected', 'likely', 'uncertain', 'unlikely', 'not_affected'))");
                    table.ForeignKey(
                        name: "fk_assessment_feedback_assessment_assessment_id",
                        column: x => x.assessment_id,
                        principalSchema: "assess",
                        principalTable: "assessment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assessment_feedback_finding_finding_id",
                        column: x => x.finding_id,
                        principalSchema: "assess",
                        principalTable: "finding",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_feedback_assessment",
                schema: "assess",
                table: "assessment_feedback",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_feedback_finding",
                schema: "assess",
                table: "assessment_feedback",
                columns: new[] { "finding_id", "created_at" });

            migrationBuilder.Sql("""
                ALTER TABLE assess.assessment_feedback ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant ON assess.assessment_feedback
                    USING (organization_id = platform.current_organization_id())
                    WITH CHECK (organization_id = platform.current_organization_id());

                -- System operation "quality of the model" (master plan 7.9, 9.2, ADR 029; task 4.7): per provider, model
                -- and prompt version over all organisations, how many assessments and verdicts there are and how many of
                -- the verdicts people confirmed or corrected. Counts only.
                CREATE FUNCTION assess.model_feedback_counts()
                    RETURNS TABLE (provider text, model text, prompt_version text, assessments bigint, verdicts bigint, confirmed bigint, corrected bigint)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT a.provider, a.model, a.prompt_version, count(*),
                               coalesce(sum(jsonb_array_length(a.output -> 'verdicts')), 0)::bigint,
                               coalesce(sum(r.confirmed), 0)::bigint, coalesce(sum(r.corrected), 0)::bigint
                        FROM assess.assessment a
                        LEFT JOIN LATERAL (
                            SELECT count(*) FILTER (WHERE f.kind = 'confirmed') AS confirmed, count(*) FILTER (WHERE f.kind = 'corrected') AS corrected
                            FROM assess.assessment_feedback f WHERE f.assessment_id = a.id) r ON true
                        WHERE a.engine = 'rules+model'
                        GROUP BY a.provider, a.model, a.prompt_version
                    $$;

                -- Live signals (task 3.11) also when the model's assessment arrives or a finding changes without a
                -- history row, e.g. its verdict: the pages show the model's answers as soon as they are stored.
                CREATE TRIGGER signal_organization_changed AFTER INSERT ON assess.assessment
                    FOR EACH ROW EXECUTE FUNCTION assess.signal_organization_changed();
                CREATE TRIGGER signal_organization_changed AFTER INSERT OR UPDATE ON assess.finding
                    FOR EACH ROW EXECUTE FUNCTION assess.signal_organization_changed();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER signal_organization_changed ON assess.finding;
                DROP TRIGGER signal_organization_changed ON assess.assessment;
                DROP FUNCTION assess.model_feedback_counts();
                """);

            migrationBuilder.DropTable(
                name: "assessment_feedback",
                schema: "assess");
        }
    }
}
