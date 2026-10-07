using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orientations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    year = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    attendance_opens_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    attendance_closes_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "text", nullable: false, defaultValue: "Africa/Johannesburg"),
                    retention_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orientations", x => x.id);
                    table.CheckConstraint("ck_orientations_dates", "start_date <= end_date");
                    table.CheckConstraint("ck_orientations_hours", "attendance_opens_at < attendance_closes_at");
                    table.CheckConstraint("ck_orientations_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_orientations_retention", "retention_due_at > created_at");
                    table.CheckConstraint("ck_orientations_timezone", "btrim(time_zone_id) <> ''");
                    table.CheckConstraint("ck_orientations_year", "year BETWEEN 2020 AND 2200");
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_type = table.Column<string>(type: "text", nullable: false),
                    institution_number = table.Column<string>(type: "text", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    surname = table.Column<string>(type: "text", nullable: false),
                    initials = table.Column<string>(type: "text", nullable: true),
                    age_group = table.Column<string>(type: "text", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    ethnicity = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                    table.CheckConstraint("ck_people_age_group", "age_group IS NULL OR age_group IN ('C1', 'C2', 'C3', 'C4', 'C5')");
                    table.CheckConstraint("ck_people_ethnicity", "ethnicity IS NULL OR ethnicity IN ('White', 'African', 'Coloured', 'Indian', 'Other')");
                    table.CheckConstraint("ck_people_first_name", "btrim(first_name) <> ''");
                    table.CheckConstraint("ck_people_gender", "gender IS NULL OR gender IN ('Male', 'Female', 'Other')");
                    table.CheckConstraint("ck_people_institution_number", "institution_number IS NULL OR institution_number ~ '^2[0-9]{8}$'");
                    table.CheckConstraint("ck_people_number_required", "person_type = 'Admin' OR institution_number IS NOT NULL");
                    table.CheckConstraint("ck_people_student_fields", "person_type <> 'Student' OR (initials IS NOT NULL AND btrim(initials) <> '' AND age_group IS NOT NULL AND gender IS NOT NULL AND ethnicity IS NOT NULL)");
                    table.CheckConstraint("ck_people_surname", "btrim(surname) <> ''");
                    table.CheckConstraint("ck_people_type", "person_type IN ('Admin', 'Mentor', 'Student')");
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    orientation_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.UniqueConstraint("ak_groups_id_orientation_id", x => new { x.id, x.orientation_id });
                    table.CheckConstraint("ck_groups_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_groups_normalized_name", "normalized_name = lower(btrim(name))");
                    table.ForeignKey(
                        name: "fk_groups_orientations_orientation_id",
                        column: x => x.orientation_id,
                        principalTable: "orientations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_emails",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_id = table.Column<long>(type: "bigint", nullable: false),
                    email_type = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    normalized_email = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_emails", x => x.id);
                    table.CheckConstraint("ck_person_emails_format", "position('@' in email) > 1");
                    table.CheckConstraint("ck_person_emails_normalized", "normalized_email = lower(btrim(email))");
                    table.CheckConstraint("ck_person_emails_type", "email_type IN ('Student', 'Personal', 'Login')");
                    table.ForeignKey(
                        name: "fk_person_emails_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    current_group_id = table.Column<long>(type: "bigint", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_eligible_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_admin_group", "role <> 'Admin' OR current_group_id IS NULL");
                    table.CheckConstraint("ck_accounts_password_hash", "btrim(password_hash) <> ''");
                    table.CheckConstraint("ck_accounts_role", "role IN ('Admin', 'Mentor')");
                    table.ForeignKey(
                        name: "fk_accounts_groups_current_group_id",
                        column: x => x.current_group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_accounts_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_enrollments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    orientation_id = table.Column<long>(type: "bigint", nullable: false),
                    student_id = table.Column<long>(type: "bigint", nullable: false),
                    current_group_id = table.Column<long>(type: "bigint", nullable: true),
                    pending_group_id = table.Column<long>(type: "bigint", nullable: true),
                    pending_group_effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    qr_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_enrollments", x => x.id);
                    table.UniqueConstraint("ak_student_enrollments_id_orientation_id", x => new { x.id, x.orientation_id });
                    table.CheckConstraint("ck_student_enrollments_pending_group", "(pending_group_id IS NULL) = (pending_group_effective_date IS NULL)");
                    table.CheckConstraint("ck_student_enrollments_qr_version", "qr_version > 0");
                    table.ForeignKey(
                        name: "fk_student_enrollments_groups_current_group_id_orientation_id",
                        columns: x => new { x.current_group_id, x.orientation_id },
                        principalTable: "groups",
                        principalColumns: new[] { "id", "orientation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_enrollments_groups_pending_group_id_orientation_id",
                        columns: x => new { x.pending_group_id, x.orientation_id },
                        principalTable: "groups",
                        principalColumns: new[] { "id", "orientation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_enrollments_orientations_orientation_id",
                        column: x => x.orientation_id,
                        principalTable: "orientations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_student_enrollments_people_student_id",
                        column: x => x.student_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_entity_type", "btrim(entity_type) <> ''");
                    table.CheckConstraint("ck_audit_events_event_type", "btrim(event_type) <> ''");
                    table.CheckConstraint("ck_audit_events_expiry", "expires_at > occurred_at");
                    table.ForeignKey(
                        name: "fk_audit_events_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_times", "last_seen_at >= created_at AND absolute_expires_at > created_at");
                    table.CheckConstraint("ck_sessions_token_hash", "btrim(token_hash) <> ''");
                    table.ForeignKey(
                        name: "fk_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    student_enrollment_id = table.Column<long>(type: "bigint", nullable: false),
                    orientation_id = table.Column<long>(type: "bigint", nullable: false),
                    group_id = table.Column<long>(type: "bigint", nullable: false),
                    attendance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Absent"),
                    capture_method = table.Column<string>(type: "text", nullable: false, defaultValue: "Automatic"),
                    marked_by_account_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.CheckConstraint("ck_attendance_records_capture_method", "capture_method IN ('Automatic', 'Qr', 'Manual', 'Registration')");
                    table.CheckConstraint("ck_attendance_records_status", "status IN ('Absent', 'Present')");
                    table.ForeignKey(
                        name: "fk_attendance_records_accounts_marked_by_account_id",
                        column: x => x.marked_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_attendance_records_groups_group_id_orientation_id",
                        columns: x => new { x.group_id, x.orientation_id },
                        principalTable: "groups",
                        principalColumns: new[] { "id", "orientation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_records_student_enrollments_student_enrollment_i",
                        columns: x => new { x.student_enrollment_id, x.orientation_id },
                        principalTable: "student_enrollments",
                        principalColumns: new[] { "id", "orientation_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_current_group_id",
                table: "accounts",
                column: "current_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_person_id",
                table: "accounts",
                column: "person_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_role",
                table: "accounts",
                column: "role",
                unique: true,
                filter: "role = 'Admin'");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_group_id_attendance_date_status",
                table: "attendance_records",
                columns: new[] { "group_id", "attendance_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_group_id_orientation_id",
                table: "attendance_records",
                columns: new[] { "group_id", "orientation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_marked_by_account_id",
                table: "attendance_records",
                column: "marked_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_orientation_id_attendance_date_status",
                table: "attendance_records",
                columns: new[] { "orientation_id", "attendance_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_student_enrollment_id_attendance_date",
                table: "attendance_records",
                columns: new[] { "student_enrollment_id", "attendance_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_student_enrollment_id_orientation_id",
                table: "attendance_records",
                columns: new[] { "student_enrollment_id", "orientation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_account_id",
                table: "audit_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_expires_at",
                table: "audit_events",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_groups_orientation_id_normalized_name",
                table: "groups",
                columns: new[] { "orientation_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orientations_retention_due_at",
                table: "orientations",
                column: "retention_due_at",
                filter: "purged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orientations_year",
                table: "orientations",
                column: "year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_institution_number",
                table: "people",
                column: "institution_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_surname_first_name",
                table: "people",
                columns: new[] { "surname", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_person_emails_normalized_email",
                table: "person_emails",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_person_emails_person_id_email_type",
                table: "person_emails",
                columns: new[] { "person_id", "email_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_absolute_expires_at",
                table: "sessions",
                column: "absolute_expires_at",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_one_active_per_account",
                table: "sessions",
                column: "account_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_current_group_id",
                table: "student_enrollments",
                column: "current_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_current_group_id_orientation_id",
                table: "student_enrollments",
                columns: new[] { "current_group_id", "orientation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_pending_group_id",
                table: "student_enrollments",
                column: "pending_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_pending_group_id_orientation_id",
                table: "student_enrollments",
                columns: new[] { "pending_group_id", "orientation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_student_id",
                table: "student_enrollments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_unassigned",
                table: "student_enrollments",
                columns: new[] { "orientation_id", "student_id" },
                unique: true,
                filter: "current_group_id IS NULL AND removed_at IS NULL");

            // Supabase exposes the public schema through its Data API. No policies are
            // created because OriSync's browser clients must use the ASP.NET Core API.
            foreach (var tableName in new[]
            {
                "orientations",
                "groups",
                "people",
                "person_emails",
                "accounts",
                "student_enrollments",
                "attendance_records",
                "sessions",
                "audit_events"
            })
            {
                migrationBuilder.Sql($"ALTER TABLE public.{tableName} ENABLE ROW LEVEL SECURITY;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "person_emails");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "student_enrollments");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "orientations");
        }
    }
}
