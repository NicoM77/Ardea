using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_item",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subject_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    entry_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    shared_aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_item", x => x.id);
                    table.CheckConstraint("ck_review_item_kind", "kind IN ('cve_conflict')");
                    table.CheckConstraint("ck_review_item_state", "state IN ('open', 'kept_separate')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_item_state",
                schema: "intel",
                table: "review_item",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ux_review_item_subject",
                schema: "intel",
                table: "review_item",
                columns: new[] { "kind", "subject_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_item",
                schema: "intel");
        }
    }
}
