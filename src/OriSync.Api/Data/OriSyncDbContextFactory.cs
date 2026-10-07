using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OriSync.Api.Data;

public sealed class OriSyncDbContextFactory : IDesignTimeDbContextFactory<OriSyncDbContext>
{
    public OriSyncDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OriSync")
            ?? "Host=localhost;Port=5432;Database=orisync;Username=postgres";

        var options = new DbContextOptionsBuilder<OriSyncDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OriSyncDbContext(options);
    }
}
