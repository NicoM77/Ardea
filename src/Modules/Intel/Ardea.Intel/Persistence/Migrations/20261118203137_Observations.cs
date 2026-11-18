using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Observations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parser_version",
                schema: "intel",
                table: "source_record",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarantine_reason",
                schema: "intel",
                table: "source_record",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "observation",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    source_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_revision = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    claims = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parser_version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observation", x => x.id);
                    table.CheckConstraint("ck_observation_kind", "kind IN ('vulnerability', 'advisory', 'warning')");
                    table.ForeignKey(
                        name: "fk_observation_source_record_source_record_id",
                        column: x => x.source_record_id,
                        principalSchema: "intel",
                        principalTable: "source_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_observation_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "intel",
                        principalTable: "source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_observation_aliases",
                schema: "intel",
                table: "observation",
                column: "aliases")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_observation_source_id",
                schema: "intel",
                table: "observation",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ux_observation_source_record",
                schema: "intel",
                table: "observation",
                column: "source_record_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observation",
                schema: "intel");

            migrationBuilder.DropColumn(
                name: "parser_version",
                schema: "intel",
                table: "source_record");

            migrationBuilder.DropColumn(
                name: "quarantine_reason",
                schema: "intel",
                table: "source_record");
        }
    }
}
