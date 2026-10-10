using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mentor_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    group_id = table.Column<long>(type: "bigint", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    normalized_email = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_discard_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purge_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentor_invitations", x => x.id);
                    table.CheckConstraint("ck_mentor_invitations_attempts", "attempt_count BETWEEN 0 AND 20");
                    table.CheckConstraint("ck_mentor_invitations_delivery_times", "available_at >= created_at AND delivery_discard_after > created_at AND (sent_at IS NULL OR sent_at >= created_at) AND (expires_at IS NULL OR (sent_at IS NOT NULL AND expires_at > sent_at))");
                    table.CheckConstraint("ck_mentor_invitations_delivery_version", "delivery_version > 0");
                    table.CheckConstraint("ck_mentor_invitations_email", "position('@' in email) > 1 AND normalized_email = lower(btrim(email))");
                    table.CheckConstraint("ck_mentor_invitations_single_terminal_state", "accepted_at IS NULL OR cancelled_at IS NULL");
                    table.CheckConstraint("ck_mentor_invitations_terminal_times", "(accepted_at IS NULL OR accepted_at >= created_at) AND (cancelled_at IS NULL OR cancelled_at >= created_at) AND (delivery_failed_at IS NULL OR delivery_failed_at >= created_at) AND (purge_after IS NULL OR purge_after > created_at)");
                    table.CheckConstraint("ck_mentor_invitations_token", "btrim(token_hash) <> ''");
                    table.ForeignKey(
                        name: "fk_mentor_invitations_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mentor_invitations_delivery",
                table: "mentor_invitations",
                columns: new[] { "available_at", "id" },
                filter: "sent_at IS NULL AND cancelled_at IS NULL AND accepted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_invitations_group_id",
                table: "mentor_invitations",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_invitations_purge",
                table: "mentor_invitations",
                column: "purge_after",
                filter: "purge_after IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_mentor_invitations_open_email",
                table: "mentor_invitations",
                column: "normalized_email",
                unique: true,
                filter: "accepted_at IS NULL AND cancelled_at IS NULL");

            // The browser never connects to PostgreSQL directly. Enabling RLS
            // keeps the same backend-only guard used by every OriSync table.
            migrationBuilder.Sql(
                "ALTER TABLE mentor_invitations ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mentor_invitations");
        }
    }
}
