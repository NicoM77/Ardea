using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Signals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entry_signal",
                schema: "intel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    scheme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kev_listed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    kev_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    kev_known_ransomware = table.Column<bool>(type: "boolean", nullable: true),
                    epss_score = table.Column<double>(type: "double precision", nullable: true),
                    epss_percentile = table.Column<double>(type: "double precision", nullable: true),
                    epss_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entry_signal", x => x.id);
                    table.CheckConstraint("ck_entry_signal_epss_class", "epss_class IN ('high', 'low')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_entry_signal",
                schema: "intel",
                table: "entry_signal",
                columns: new[] { "scheme", "value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entry_signal",
                schema: "intel");
        }
    }
}
