using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Assessment.Persistence.Migrations
{
    public partial class NoticeGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notice_list",
                schema: "assess",
                table: "notice");

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

            migrationBuilder.AddColumn<bool>(
                name: "decided_kev",
                schema: "assess",
                table: "notice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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

            migrationBuilder.AddColumn<bool>(
                name: "exploited",
                schema: "assess",
                table: "notice",
                type: "boolean",
                nullable: false,
                computedColumnSql: "exploitation = 'active'",
                stored: true);

            migrationBuilder.AddColumn<double>(
                name: "score_rank",
                schema: "assess",
                table: "notice",
                type: "double precision",
                nullable: false,
                computedColumnSql: "coalesce(cvss_score, -1)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_notice_list",
                schema: "assess",
                table: "notice",
                columns: new[] { "organization_id", "state", "is_backfill", "exploited", "score_rank", "raised_at", "id" },
                descending: new[] { false, false, false, true, true, true, true });

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
            migrationBuilder.Sql("UPDATE assess.notice SET state = 'open' WHERE state = 'withdrawn';");

            migrationBuilder.DropIndex(
                name: "ix_notice_list",
                schema: "assess",
                table: "notice");

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
                name: "exploited",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "score_rank",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "cvss_score",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "decided_exploitation",
                schema: "assess",
                table: "notice");

            migrationBuilder.DropColumn(
                name: "decided_kev",
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

            migrationBuilder.CreateIndex(
                name: "ix_notice_list",
                schema: "assess",
                table: "notice",
                columns: new[] { "organization_id", "state", "raised_at" });

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
