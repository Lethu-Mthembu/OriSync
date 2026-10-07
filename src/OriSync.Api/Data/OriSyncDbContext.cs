using Microsoft.EntityFrameworkCore;
using OriSync.Api.Domain;

namespace OriSync.Api.Data;

public sealed class OriSyncDbContext(DbContextOptions<OriSyncDbContext> options) : DbContext(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OriSyncDbContext).Assembly);
    }
}
