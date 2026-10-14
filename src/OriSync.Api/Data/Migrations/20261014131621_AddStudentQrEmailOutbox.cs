using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentQrEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_qr_email_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    student_enrollment_id = table.Column<long>(type: "bigint", nullable: false),
                    qr_version = table.Column<int>(type: "integer", nullable: false),
                    recipient_email = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discard_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_qr_email_outbox", x => x.id);
                    table.CheckConstraint("ck_student_qr_email_outbox_attempts", "attempt_count BETWEEN 0 AND 20");
                    table.CheckConstraint("ck_student_qr_email_outbox_qr_version", "qr_version > 0");
                    table.CheckConstraint("ck_student_qr_email_outbox_recipient", "position('@' in recipient_email) > 1");
                    table.CheckConstraint("ck_student_qr_email_outbox_terminal_state", "sent_at IS NULL OR discarded_at IS NULL");
                    table.CheckConstraint("ck_student_qr_email_outbox_times", "available_at >= created_at AND discard_after > created_at AND (sent_at IS NULL OR sent_at >= created_at) AND (discarded_at IS NULL OR discarded_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_student_qr_email_outbox_student_enrollments_student_enrollm",
                        column: x => x.student_enrollment_id,
                        principalTable: "student_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_qr_email_outbox_available_at_id",
                table: "student_qr_email_outbox",
                columns: new[] { "available_at", "id" },
                filter: "sent_at IS NULL AND discarded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_student_qr_email_outbox_discard_after",
                table: "student_qr_email_outbox",
                column: "discard_after",
                filter: "sent_at IS NULL AND discarded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_student_qr_email_outbox_student_enrollment_id",
                table: "student_qr_email_outbox",
                column: "student_enrollment_id");

            migrationBuilder.Sql(
                "ALTER TABLE student_qr_email_outbox ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_qr_email_outbox");
        }
    }
}
