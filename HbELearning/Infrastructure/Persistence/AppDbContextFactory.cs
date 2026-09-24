// File: Infrastructure/Persistence/AppDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HbELearning.Infrastructure.Persistence;

/// <summary>
/// Cho phép lệnh "dotnet ef" tạo DbContext mà không cần khởi động toàn bộ ứng dụng.
/// Chỉ định provider khi cần:  dotnet ef migrations add Init -- --provider Oracle
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var provider = DbProviderSetup.Resolve(configuration, args);

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderSetup.Apply(builder, provider, configuration);

        return new AppDbContext(builder.Options);
    }
}
