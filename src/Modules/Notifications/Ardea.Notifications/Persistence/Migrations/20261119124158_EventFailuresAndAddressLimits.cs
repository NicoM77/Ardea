using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventFailuresAndAddressLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                schema: "notify",
                table: "pending_event",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at",
                schema: "notify",
                table: "pending_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                schema: "notify",
                table: "pending_event",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmation_sent_at",
                schema: "notify",
                table: "external_address",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "confirmations_sent",
                schema: "notify",
                table: "external_address",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                -- Safety net of the hand-off (ADR 038): which organisations have events waiting since before a point in
                -- time and not set aside, for the instance's minute schedule to queue their evaluation. Ids only.
                CREATE FUNCTION notify.organizations_with_waiting_events(p_before timestamptz)
                    RETURNS TABLE (organization_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT DISTINCT e.organization_id FROM notify.pending_event e WHERE e.failed_at IS NULL AND e.at < p_before
                    $$;

                -- Instance status (ADR 038): how many events were set aside after repeated failures. A count only.
                CREATE FUNCTION notify.failed_event_count() RETURNS bigint
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$ SELECT count(*) FROM notify.pending_event e WHERE e.failed_at IS NOT NULL $$;

                -- Limit on confirmation mails (ADR 038): how many organisations asked this address since a point in
                -- time, over the instance. A count only.
                CREATE FUNCTION notify.address_confirmations_since(p_normalized_address text, p_since timestamptz) RETURNS bigint
                    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
                    AS $$
                        SELECT count(*) FROM notify.external_address a
                        WHERE a.normalized_address = p_normalized_address AND a.confirmation_sent_at > p_since
                    $$;

                CREATE INDEX ix_external_address_normalized ON notify.external_address (normalized_address);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX notify.ix_external_address_normalized;
                DROP FUNCTION notify.address_confirmations_since(text, timestamptz);
                DROP FUNCTION notify.failed_event_count();
                DROP FUNCTION notify.organizations_with_waiting_events(timestamptz);
                """);

            migrationBuilder.DropColumn(
                name: "attempts",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.DropColumn(
                name: "failed_at",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.DropColumn(
                name: "last_error",
                schema: "notify",
                table: "pending_event");

            migrationBuilder.DropColumn(
                name: "confirmation_sent_at",
                schema: "notify",
                table: "external_address");

            migrationBuilder.DropColumn(
                name: "confirmations_sent",
                schema: "notify",
                table: "external_address");
        }
    }
}
