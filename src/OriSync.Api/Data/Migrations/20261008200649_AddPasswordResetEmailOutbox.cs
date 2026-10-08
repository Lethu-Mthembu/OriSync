using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_password_reset_otps_times",
                table: "password_reset_otps");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at",
                table: "password_reset_otps",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "code_nonce",
                table: "password_reset_otps",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "password_reset_email_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    password_reset_otp_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_email = table.Column<string>(type: "text", nullable: false),
                    recipient_first_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discard_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_email_outbox", x => x.id);
                    table.CheckConstraint("ck_password_reset_email_outbox_attempts", "attempt_count BETWEEN 0 AND 20");
                    table.CheckConstraint("ck_password_reset_email_outbox_name", "btrim(recipient_first_name) <> ''");
                    table.CheckConstraint("ck_password_reset_email_outbox_recipient", "position('@' in recipient_email) > 1");
                    table.CheckConstraint("ck_password_reset_email_outbox_terminal_state", "sent_at IS NULL OR discarded_at IS NULL");
                    table.CheckConstraint("ck_password_reset_email_outbox_times", "available_at >= created_at AND discard_after > created_at AND (sent_at IS NULL OR sent_at >= created_at) AND (discarded_at IS NULL OR discarded_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_password_reset_email_outbox_password_reset_otps_password_re",
                        column: x => x.password_reset_otp_id,
                        principalTable: "password_reset_otps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "ALTER TABLE password_reset_email_outbox ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_password_reset_otps_times",
                table: "password_reset_otps",
                sql: "((sent_at IS NULL AND expires_at IS NULL) OR (sent_at IS NOT NULL AND expires_at > sent_at)) AND (consumed_at IS NULL OR consumed_at >= requested_at)");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_email_outbox_available_at_id",
                table: "password_reset_email_outbox",
                columns: new[] { "available_at", "id" },
                filter: "sent_at IS NULL AND discarded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_email_outbox_discard_after",
                table: "password_reset_email_outbox",
                column: "discard_after",
                filter: "sent_at IS NULL AND discarded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_email_outbox_password_reset_otp_id",
                table: "password_reset_email_outbox",
                column: "password_reset_otp_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_email_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_password_reset_otps_times",
                table: "password_reset_otps");

            migrationBuilder.DropColumn(
                name: "code_nonce",
                table: "password_reset_otps");

            migrationBuilder.Sql(
                "DELETE FROM password_reset_otps WHERE expires_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at",
                table: "password_reset_otps",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_password_reset_otps_times",
                table: "password_reset_otps",
                sql: "expires_at > requested_at AND (sent_at IS NULL OR sent_at >= requested_at) AND (consumed_at IS NULL OR consumed_at >= requested_at)");
        }
    }
}
