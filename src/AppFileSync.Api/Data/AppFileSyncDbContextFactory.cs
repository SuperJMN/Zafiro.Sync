using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppFileSync.Api.Data;

public sealed class AppFileSyncDbContextFactory : IDesignTimeDbContextFactory<AppFileSyncDbContext>
{
    public AppFileSyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppFileSyncDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=appfilesync;Username=appfilesync;Password=appfilesync")
            .Options;

        return new AppFileSyncDbContext(options);
    }
}
