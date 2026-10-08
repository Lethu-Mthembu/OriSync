using Microsoft.EntityFrameworkCore;
using Npgsql;
using OriSync.Api.Data;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class DatabaseSchemaTests : IAsyncLifetime
{
    private static readonly string[] ExpectedTables =
    [
        "accounts",
        "attendance_records",
        "audit_events",
        "data_protection_keys",
        "groups",
        "orientations",
        "people",
        "person_emails",
        "sessions",
        "student_enrollments"
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task InitialMigrationCreatesGuardedBackendOnlySchema()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        var tableSecurity = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var tablesCommand = new NpgsqlCommand(
            """
            SELECT tablename, rowsecurity
            FROM pg_tables
            WHERE schemaname = 'public'
              AND tablename <> '__EFMigrationsHistory'
            ORDER BY tablename;
            """,
            connection))
        await using (var reader = await tablesCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tableSecurity.Add(reader.GetString(0), reader.GetBoolean(1));
            }
        }

        Assert.Equal(ExpectedTables, tableSecurity.Keys);
        Assert.All(tableSecurity.Values, Assert.True);

        await using (var policiesCommand = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'public';",
            connection))
        {
            Assert.Equal(0L, (long)(await policiesCommand.ExecuteScalarAsync())!);
        }

        await AssertSqlStateAsync(
            connection,
            """
            INSERT INTO people
                (person_type, institution_number, first_name, surname, initials, age_group, gender, ethnicity)
            VALUES
                ('Student', '123456789', 'Invalid', 'Number', 'I', 'C1', 'Other', 'Other');
            """,
            PostgresErrorCodes.CheckViolation);

        await AssertSqlStateAsync(
            connection,
            """
            INSERT INTO people
                (person_type, institution_number, first_name, surname)
            VALUES
                ('Student', '223456787', 'Missing', 'Demographics');
            """,
            PostgresErrorCodes.CheckViolation);

        long studentId;
        await using (var validPersonCommand = new NpgsqlCommand(
            """
            INSERT INTO people
                (person_type, institution_number, first_name, surname, initials, age_group, gender, ethnicity)
            VALUES
                ('Student', '223456789', 'Jane', 'Doe', 'J', 'C1', 'Female', 'African')
            RETURNING id;
            """,
            connection))
        {
            studentId = (long)(await validPersonCommand.ExecuteScalarAsync())!;
        }

        await using (var emailsCommand = new NpgsqlCommand(
            """
            INSERT INTO person_emails
                (person_id, email_type, email, normalized_email)
            VALUES
                ($1, 'Student', 'Jane.Doe@tut4life.ac.za', 'jane.doe@tut4life.ac.za'),
                ($1, 'Personal', 'Jane.Doe@gmail.com', 'jane.doe@gmail.com');
            """,
            connection))
        {
            emailsCommand.Parameters.AddWithValue(studentId);
            await emailsCommand.ExecuteNonQueryAsync();
        }

        await AssertSqlStateAsync(
            connection,
            """
            INSERT INTO people
                (person_type, institution_number, first_name, surname, initials, age_group, gender, ethnicity)
            VALUES
                ('Mentor', '223456789', 'Duplicate', 'Number', NULL, NULL, NULL, NULL);
            """,
            PostgresErrorCodes.UniqueViolation);

        long mentorId;
        await using (var mentorCommand = new NpgsqlCommand(
            """
            INSERT INTO people
                (person_type, institution_number, first_name, surname)
            VALUES
                ('Mentor', '223456788', 'John', 'Mentor')
            RETURNING id;
            """,
            connection))
        {
            mentorId = (long)(await mentorCommand.ExecuteScalarAsync())!;
        }

        await AssertSqlStateAsync(
            connection,
            $"""
            INSERT INTO person_emails
                (person_id, email_type, email, normalized_email)
            VALUES
                ({mentorId}, 'Login', 'JANE.DOE@GMAIL.COM', 'jane.doe@gmail.com');
            """,
            PostgresErrorCodes.UniqueViolation);
    }

    private OriSyncDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OriSyncDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OriSyncDbContext(options);
    }

    private static async Task AssertSqlStateAsync(
        NpgsqlConnection connection,
        string sql,
        string expectedSqlState)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());

        Assert.Equal(expectedSqlState, exception.SqlState);
    }
}
