using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class AdminBootstrapCommandTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_bootstrap_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task BootstrapCreatesOnlyOneAdminAndPrintsRecoveryCodeOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
        services.AddDbContext<OriSyncDbContext>(options =>
            options.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:AdminEmail"] = "admin@orisync.test",
                ["Bootstrap:AdminPassword"] = "bootstrap-password",
                ["Bootstrap:AdminFirstName"] = "Admin",
                ["Bootstrap:AdminSurname"] = "User"
            })
            .Build();
        using var output = new StringWriter();

        Assert.Equal(
            0,
            await AdminBootstrapCommand.RunAsync(provider, configuration, output));
        Assert.Contains("Recovery code (shown once):", output.ToString(), StringComparison.Ordinal);

        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var admin = await dbContext.Accounts
                .Include(account => account.Person)
                    .ThenInclude(person => person.Emails)
                .SingleAsync(account => account.Role == AccountRole.Admin);
            Assert.Equal("admin@orisync.test", admin.Person.Emails.Single().NormalizedEmail);
            Assert.NotNull(admin.RecoveryCodeHash);
            Assert.True(admin.MustChangePassword);
            Assert.DoesNotContain("bootstrap-password", admin.PasswordHash, StringComparison.Ordinal);
        }

        using var secondOutput = new StringWriter();
        Assert.Equal(
            3,
            await AdminBootstrapCommand.RunAsync(provider, configuration, secondOutput));
        Assert.DoesNotContain("Recovery code (shown once):", secondOutput.ToString(), StringComparison.Ordinal);
    }
}
