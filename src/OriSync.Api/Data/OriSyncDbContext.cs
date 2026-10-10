using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Domain;

namespace OriSync.Api.Data;

public sealed class OriSyncDbContext(DbContextOptions<OriSyncDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Orientation> Orientations => Set<Orientation>();
    public DbSet<OrientationGroup> Groups => Set<OrientationGroup>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonEmail> PersonEmails => Set<PersonEmail>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AccountSession> Sessions => Set<AccountSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<PasswordResetEmailOutbox> PasswordResetEmailOutbox => Set<PasswordResetEmailOutbox>();
    public DbSet<MentorInvitation> MentorInvitations => Set<MentorInvitation>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OriSyncDbContext).Assembly);
    }
}
