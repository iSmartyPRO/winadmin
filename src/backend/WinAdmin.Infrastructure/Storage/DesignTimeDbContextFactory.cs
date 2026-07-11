using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>
/// Используется только инструментами EF (dotnet ef) для создания контекста
/// во время разработки — чтобы не запускать стартовую логику API.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WinAdminDbContext>
{
    public WinAdminDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WinAdminDbContext>()
            .UseSqlite("Data Source=WinAdmin-design.db")
            .Options;
        return new WinAdminDbContext(options);
    }
}
