// File: Infrastructure/Files/AppPaths.cs

namespace HbELearning.Infrastructure.Files;

/// <summary>Hiện thực IAppPaths bằng môi trường chạy của ASP.NET Core.</summary>
public sealed class AppPaths : IAppPaths
{
    private readonly IWebHostEnvironment _env;

    public AppPaths(IWebHostEnvironment env) => _env = env;

    public string WebRootPath => _env.WebRootPath;

    public string ContentRootPath => _env.ContentRootPath;
}
