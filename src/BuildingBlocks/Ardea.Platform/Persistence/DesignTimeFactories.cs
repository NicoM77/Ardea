using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Platform.Persistence;

internal sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseArdeaNpgsql("Host=localhost;Database=ardea", PlatformDbContext.Schema)
            .Options);
}
