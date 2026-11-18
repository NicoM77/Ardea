using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ardea.Iam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization", x => x.id);
                    table.CheckConstraint("ck_organization_slug", "slug ~ '^[a-z0-9]([a-z0-9-]{0,48}[a-z0-9])?$'");
                });

            migrationBuilder.CreateTable(
                name: "invitation",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation", x => x.id);
                    table.CheckConstraint("ck_invitation_role", "role IN ('admin', 'member', 'viewer')");
                    table.ForeignKey(
                        name: "fk_invitation_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "iam",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitation_user_invited_by",
                        column: x => x.invited_by,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership", x => x.id);
                    table.CheckConstraint("ck_membership_role", "role IN ('owner', 'admin', 'member', 'viewer')");
                    table.ForeignKey(
                        name: "fk_membership_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "iam",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membership_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_invited_by",
                schema: "iam",
                table: "invitation",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ux_invitation_org_email",
                schema: "iam",
                table: "invitation",
                columns: new[] { "organization_id", "normalized_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invitation_token",
                schema: "iam",
                table: "invitation",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membership_user",
                schema: "iam",
                table: "membership",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_membership_org_user",
                schema: "iam",
                table: "membership",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_membership_owner",
                schema: "iam",
                table: "membership",
                column: "organization_id",
                unique: true,
                filter: "role = 'owner'");

            migrationBuilder.CreateIndex(
                name: "ux_organization_slug",
                schema: "iam",
                table: "organization",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "membership",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "organization",
                schema: "iam");
        }
    }
}
