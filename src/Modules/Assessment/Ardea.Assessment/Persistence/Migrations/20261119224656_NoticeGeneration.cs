using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    /// <summary>
    /// Task 5a.3 (master plan 17.3, ADR 047): notices are written by the assessment. A notice keeps the entry's score and
    /// exploitation for the order of the list, the exploitation when it was answered (E22: it comes back only when the entry is
    /// exploited later), whether a reverse search raised it, and may be withdrawn; history rows name their notice. New columns
    /// have defaults and the checks only widen, so the previous build keeps working during the upgrade (master plan 4.11).
    /// </summary>
    public partial class NoticeGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_decided",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_state",
                schema: "assess",
                table: "notice");

            migrationBuilder.AddColumn<double>(
                name: "cvss_score",
                schema: "assess",
                table: "notice",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_exploitation",
                schema: "assess",
                table: "notice",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exploitation",
                schema: "assess",
                table: "notice",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<bool>(
                name: "is_backfill",
                schema: "assess",
                table: "notice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "notice_id",
                schema: "assess",
                table: "assess_event",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_decided",
                schema: "assess",
                table: "notice",
                sql: "state IN ('open', 'withdrawn') OR (decided_by IS NOT NULL AND decided_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_exploitation",
                schema: "assess",
                table: "notice",
                sql: "exploitation IN ('none', 'poc', 'active') AND (decided_exploitation IS NULL OR decided_exploitation IN ('none', 'poc', 'active'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_state",
                schema: "assess",
                table: "notice",
                sql: "state IN ('open', 'affects_us', 'not_affected', 'withdrawn')");

            migrationBuilder.CreateIndex(
                name: "ix_assess_event_notice",
                schema: "assess",
                table: "assess_event",
                columns: new[] { "notice_id", "at" });

            // The notices of the migration NoticesAndOpenOrDone take their entry's score and exploitation; Intel migrates before
            // Assess (master plan 4.11, ADR 046 decision 10). A test database without Intel skips it.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('intel.security_entry') IS NOT NULL THEN
                        UPDATE assess.notice n SET cvss_score = e.cvss_score, exploitation = e.exploitation
                        FROM intel.security_entry e WHERE e.id = n.entry_id;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_decided",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_exploitation",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notice_state",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropIndex(
                name: "ix_assess_event_notice",
                schema: "assess",
                table: "assess_event");

            migrationBuilder.DropColumn(
                name: "cvss_score",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "decided_exploitation",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "exploitation",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "is_backfill",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "notice_id",
                schema: "assess",
                table: "assess_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_decided",
                schema: "assess",
                table: "notice",
                sql: "state = 'open' OR (decided_by IS NOT NULL AND decided_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notice_state",
                schema: "assess",
                table: "notice",
                sql: "state IN ('open', 'affects_us', 'not_affected')");
        }
    }
}
