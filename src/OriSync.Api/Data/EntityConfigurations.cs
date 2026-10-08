using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OriSync.Api.Domain;

namespace OriSync.Api.Data;

internal sealed class OrientationConfiguration : IEntityTypeConfiguration<Orientation>
{
    public void Configure(EntityTypeBuilder<Orientation> builder)
    {
        builder.ToTable("orientations", table =>
        {
            table.HasCheckConstraint("ck_orientations_year", "year BETWEEN 2020 AND 2200");
            table.HasCheckConstraint("ck_orientations_dates", "start_date <= end_date");
            table.HasCheckConstraint(
                "ck_orientations_hours",
                "attendance_opens_at < attendance_closes_at");
            table.HasCheckConstraint("ck_orientations_name", "btrim(name) <> ''");
            table.HasCheckConstraint("ck_orientations_timezone", "btrim(time_zone_id) <> ''");
            table.HasCheckConstraint(
                "ck_orientations_retention",
                "retention_due_at > created_at");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.HasIndex(x => x.Year).IsUnique();
        builder.Property(x => x.Name).HasColumnType("text").IsRequired();
        builder.Property(x => x.StartDate).HasColumnType("date");
        builder.Property(x => x.EndDate).HasColumnType("date");
        builder.Property(x => x.AttendanceOpensAt).HasColumnType("time without time zone");
        builder.Property(x => x.AttendanceClosesAt).HasColumnType("time without time zone");
        builder.Property(x => x.TimeZoneId).HasColumnType("text").HasDefaultValue("Africa/Johannesburg");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.RetentionDueAt).HasFilter("purged_at IS NULL");
    }
}

internal sealed class OrientationGroupConfiguration : IEntityTypeConfiguration<OrientationGroup>
{
    public void Configure(EntityTypeBuilder<OrientationGroup> builder)
    {
        builder.ToTable("groups", table =>
        {
            table.HasCheckConstraint("ck_groups_name", "btrim(name) <> ''");
            table.HasCheckConstraint(
                "ck_groups_normalized_name",
                "normalized_name = lower(btrim(name))");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.HasAlternateKey(x => new { x.Id, x.OrientationId });
        builder.Property(x => x.Name).HasColumnType("text").IsRequired();
        builder.Property(x => x.NormalizedName).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.OrientationId, x.NormalizedName }).IsUnique();
        builder.HasOne(x => x.Orientation)
            .WithMany(x => x.Groups)
            .HasForeignKey(x => x.OrientationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people", table =>
        {
            table.HasCheckConstraint(
                "ck_people_type",
                "person_type IN ('Admin', 'Mentor', 'Student')");
            table.HasCheckConstraint(
                "ck_people_institution_number",
                "institution_number IS NULL OR institution_number ~ '^2[0-9]{8}$'");
            table.HasCheckConstraint(
                "ck_people_number_required",
                "person_type = 'Admin' OR institution_number IS NOT NULL");
            table.HasCheckConstraint("ck_people_first_name", "btrim(first_name) <> ''");
            table.HasCheckConstraint("ck_people_surname", "btrim(surname) <> ''");
            table.HasCheckConstraint(
                "ck_people_student_fields",
                "person_type <> 'Student' OR " +
                "(initials IS NOT NULL AND btrim(initials) <> '' AND age_group IS NOT NULL " +
                "AND gender IS NOT NULL AND ethnicity IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_people_age_group",
                "age_group IS NULL OR age_group IN ('C1', 'C2', 'C3', 'C4', 'C5')");
            table.HasCheckConstraint(
                "ck_people_gender",
                "gender IS NULL OR gender IN ('Male', 'Female', 'Other')");
            table.HasCheckConstraint(
                "ck_people_ethnicity",
                "ethnicity IS NULL OR ethnicity IN ('White', 'African', 'Coloured', 'Indian', 'Other')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.PersonType).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.InstitutionNumber).HasColumnType("text");
        builder.Property(x => x.FirstName).HasColumnType("text").IsRequired();
        builder.Property(x => x.Surname).HasColumnType("text").IsRequired();
        builder.Property(x => x.Initials).HasColumnType("text");
        builder.Property(x => x.AgeGroup).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.Gender).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.Ethnicity).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.InstitutionNumber).IsUnique();
        builder.HasIndex(x => new { x.Surname, x.FirstName });
    }
}

internal sealed class PersonEmailConfiguration : IEntityTypeConfiguration<PersonEmail>
{
    public void Configure(EntityTypeBuilder<PersonEmail> builder)
    {
        builder.ToTable("person_emails", table =>
        {
            table.HasCheckConstraint(
                "ck_person_emails_type",
                "email_type IN ('Student', 'Personal', 'Login')");
            table.HasCheckConstraint(
                "ck_person_emails_format",
                "position('@' in email) > 1");
            table.HasCheckConstraint(
                "ck_person_emails_normalized",
                "normalized_email = lower(btrim(email))");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.EmailType).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.Email).HasColumnType("text").IsRequired();
        builder.Property(x => x.NormalizedEmail).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.HasIndex(x => new { x.PersonId, x.EmailType }).IsUnique();
        builder.HasOne(x => x.Person)
            .WithMany(x => x.Emails)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_role", "role IN ('Admin', 'Mentor')");
            table.HasCheckConstraint("ck_accounts_password_hash", "btrim(password_hash) <> ''");
            table.HasCheckConstraint(
                "ck_accounts_admin_group",
                "role <> 'Admin' OR current_group_id IS NULL");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.Role).HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.PasswordHash).HasColumnType("text").IsRequired();
        builder.Property(x => x.RecoveryCodeHash).HasColumnType("text");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.PersonId).IsUnique();
        builder.HasIndex(x => x.CurrentGroupId);
        builder.HasIndex(x => x.Role)
            .IsUnique()
            .HasFilter("role = 'Admin'");
        builder.HasOne(x => x.Person)
            .WithOne()
            .HasForeignKey<Account>(x => x.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CurrentGroup)
            .WithMany()
            .HasForeignKey(x => x.CurrentGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentEnrollmentConfiguration : IEntityTypeConfiguration<StudentEnrollment>
{
    public void Configure(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.ToTable("student_enrollments", table =>
        {
            table.HasCheckConstraint("ck_student_enrollments_qr_version", "qr_version > 0");
            table.HasCheckConstraint(
                "ck_student_enrollments_pending_group",
                "(pending_group_id IS NULL) = (pending_group_effective_date IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.HasAlternateKey(x => new { x.Id, x.OrientationId });
        builder.Property(x => x.QrVersion).HasDefaultValue(1);
        builder.Property(x => x.EnrolledAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.OrientationId, x.StudentId }).IsUnique();
        builder.HasIndex(x => x.CurrentGroupId);
        builder.HasIndex(x => x.PendingGroupId);
        builder.HasIndex(x => new { x.OrientationId, x.StudentId })
            .HasFilter("current_group_id IS NULL AND removed_at IS NULL")
            .HasDatabaseName("ix_student_enrollments_unassigned");
        builder.HasOne(x => x.Orientation)
            .WithMany(x => x.StudentEnrollments)
            .HasForeignKey(x => x.OrientationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CurrentGroup)
            .WithMany()
            .HasForeignKey(x => new { x.CurrentGroupId, x.OrientationId })
            .HasPrincipalKey(x => new { x.Id, x.OrientationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PendingGroup)
            .WithMany()
            .HasForeignKey(x => new { x.PendingGroupId, x.OrientationId })
            .HasPrincipalKey(x => new { x.Id, x.OrientationId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("attendance_records", table =>
        {
            table.HasCheckConstraint("ck_attendance_records_status", "status IN ('Absent', 'Present')");
            table.HasCheckConstraint(
                "ck_attendance_records_capture_method",
                "capture_method IN ('Automatic', 'Qr', 'Manual', 'Registration')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnType("text")
            .HasDefaultValue(AttendanceStatus.Absent);
        builder.Property(x => x.CaptureMethod)
            .HasConversion<string>()
            .HasColumnType("text")
            .HasDefaultValue(AttendanceCaptureMethod.Automatic);
        builder.HasIndex(x => new { x.StudentEnrollmentId, x.AttendanceDate }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.AttendanceDate, x.Status });
        builder.HasIndex(x => new { x.OrientationId, x.AttendanceDate, x.Status });
        builder.HasIndex(x => x.MarkedByAccountId);
        builder.HasOne(x => x.StudentEnrollment)
            .WithMany(x => x.AttendanceRecords)
            .HasForeignKey(x => new { x.StudentEnrollmentId, x.OrientationId })
            .HasPrincipalKey(x => new { x.Id, x.OrientationId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => new { x.GroupId, x.OrientationId })
            .HasPrincipalKey(x => new { x.Id, x.OrientationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MarkedByAccount)
            .WithMany()
            .HasForeignKey(x => x.MarkedByAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AccountSessionConfiguration : IEntityTypeConfiguration<AccountSession>
{
    public void Configure(EntityTypeBuilder<AccountSession> builder)
    {
        builder.ToTable("sessions", table =>
        {
            table.HasCheckConstraint("ck_sessions_token_hash", "btrim(token_hash) <> ''");
            table.HasCheckConstraint(
                "ck_sessions_times",
                "last_seen_at >= created_at AND absolute_expires_at > created_at");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.TokenHash).HasColumnType("text").IsRequired();
        builder.Property(x => x.RevocationReason).HasColumnType("text");
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.AccountId)
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_sessions_one_active_per_account");
        builder.HasIndex(x => x.AbsoluteExpiresAt).HasFilter("revoked_at IS NULL");
        builder.HasOne(x => x.Account)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.ToTable("password_reset_otps", table =>
        {
            table.HasCheckConstraint("ck_password_reset_otps_code_hash", "btrim(code_hash) <> ''");
            table.HasCheckConstraint(
                "ck_password_reset_otps_times",
                "expires_at > requested_at AND (sent_at IS NULL OR sent_at >= requested_at) " +
                "AND (consumed_at IS NULL OR consumed_at >= requested_at)");
            table.HasCheckConstraint(
                "ck_password_reset_otps_failed_attempts",
                "failed_attempts BETWEEN 0 AND 5");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.CodeHash).HasColumnType("text").IsRequired();
        builder.HasIndex(x => new { x.AccountId, x.RequestedAt });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.Account)
            .WithMany(x => x.PasswordResetOtps)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", table =>
        {
            table.HasCheckConstraint("ck_audit_events_event_type", "btrim(event_type) <> ''");
            table.HasCheckConstraint("ck_audit_events_entity_type", "btrim(entity_type) <> ''");
            table.HasCheckConstraint("ck_audit_events_expiry", "expires_at > occurred_at");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.EventType).HasColumnType("text").IsRequired();
        builder.Property(x => x.EntityType).HasColumnType("text").IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.AccountId);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
