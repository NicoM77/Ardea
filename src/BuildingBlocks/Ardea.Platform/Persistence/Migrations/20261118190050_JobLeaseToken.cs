using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobLeaseToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_finished",
                schema: "platform",
                table: "job");

            migrationBuilder.AddColumn<Guid>(
                name: "lease_token",
                schema: "platform",
                table: "job",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_finished",
                schema: "platform",
                table: "job",
                column: "finished_at",
                filter: "state IN ('succeeded', 'failed', 'dead')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_finished",
                schema: "platform",
                table: "job");

            migrationBuilder.DropColumn(
                name: "lease_token",
                schema: "platform",
                table: "job");

            migrationBuilder.CreateIndex(
                name: "ix_job_finished",
                schema: "platform",
                table: "job",
                column: "finished_at",
                filter: "state IN ('succeeded', 'dead')");
        }
    }
}
