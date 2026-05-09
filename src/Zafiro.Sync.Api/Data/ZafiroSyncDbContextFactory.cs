using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zafiro.Sync.Api.Data;

public sealed class ZafiroSyncDbContextFactory : IDesignTimeDbContextFactory<ZafiroSyncDbContext>
{
    public ZafiroSyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ZafiroSyncDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=zafiro-sync;Username=zafiro-sync;Password=zafiro-sync")
            .Options;

        return new ZafiroSyncDbContext(options);
    }
}
