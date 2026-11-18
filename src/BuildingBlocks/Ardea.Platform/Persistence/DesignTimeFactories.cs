using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ardea.Platform.Persistence;

/// <summary>Used only by <c>dotnet ef migrations add</c>; no database is contacted.</summary>
internal sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseArdeaNpgsql("Host=localhost;Database=ardea", PlatformDbContext.Schema)
            .Options);
}
