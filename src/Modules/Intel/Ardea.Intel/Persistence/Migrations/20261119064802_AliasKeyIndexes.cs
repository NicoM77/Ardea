using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Intel.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AliasKeyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Correlation looks identifiers up as "SCHEME:value" (CorrelateEntry, WithSignalsAsync). Without
            // an index on that expression every correlation scanned all signals: about 400 000 EPSS rows.
            migrationBuilder.Sql("""
                CREATE INDEX ix_entry_alias_key ON intel.entry_alias ((scheme || ':' || value));
                CREATE INDEX ix_entry_signal_key ON intel.entry_signal ((scheme || ':' || value));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX intel.ix_entry_signal_key; DROP INDEX intel.ix_entry_alias_key;");
        }
    }
}
