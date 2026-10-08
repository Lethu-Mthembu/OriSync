using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorPasswordResetOtps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_otps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_otps", x => x.id);
                    table.CheckConstraint("ck_password_reset_otps_code_hash", "btrim(code_hash) <> ''");
                    table.CheckConstraint("ck_password_reset_otps_failed_attempts", "failed_attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_password_reset_otps_times", "expires_at > requested_at AND (sent_at IS NULL OR sent_at >= requested_at) AND (consumed_at IS NULL OR consumed_at >= requested_at)");
                    table.ForeignKey(
                        name: "fk_password_reset_otps_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("ALTER TABLE password_reset_otps ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_otps_account_id_requested_at",
                table: "password_reset_otps",
                columns: new[] { "account_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_otps_expires_at",
                table: "password_reset_otps",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_otps");
        }
    }
}
