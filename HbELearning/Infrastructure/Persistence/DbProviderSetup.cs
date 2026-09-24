// File: Infrastructure/Persistence/DbProviderSetup.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure.Persistence;

/// <summary>
/// Chọn database provider theo cấu hình "DatabaseProvider" trong appsettings.json.
/// Cho phép đổi giữa Sqlite / SqlServer / Oracle mà không sửa code nghiệp vụ.
/// </summary>
public static class DbProviderSetup
{
    public const string Oracle = "Oracle";
    public const string SqlServer = "SqlServer";
    public const string Sqlite = "Sqlite";

    /// <summary>Ưu tiên tham số dòng lệnh "--provider X", sau đó tới appsettings.json.</summary>
    public static string Resolve(IConfiguration configuration, string[]? args = null)
    {
        if (args is not null)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("--provider", StringComparison.OrdinalIgnoreCase))
                    return Normalize(args[i + 1]);
        }
        return Normalize(configuration["DatabaseProvider"]);
    }

    private static string Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        "oracle" => Oracle,
        "sqlserver" or "mssql" => SqlServer,
        _ => Sqlite
    };

    public static void Apply(DbContextOptionsBuilder options, string provider, IConfiguration configuration)
    {
        switch (provider)
        {
            case Oracle:
                options.UseOracle(Require(configuration, "OracleConnection"));
                break;

            case SqlServer:
                // LocalDB tu tat sau vai phut khong ai dung; lan chay ke tiep phai cho instance khoi dong lai
                // (do duoc hon 100 giay tren may cham) — vuot xa han cho mac dinh 15 giay cua SqlClient nen
                // ung dung chet ngay o EnsureCreatedAsync. Han cho dai nam trong chuoi ket noi (Connect
                // Timeout), con EnableRetryOnFailure lo cac loi ket noi chap chon con lai.
                options.UseSqlServer(Require(configuration, "SqlServerConnection"),
                    sql => sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null));
                break;

            default:
                options.UseSqlite(Require(configuration, "SqliteConnection"));
                break;
        }
    }

    private static string Require(IConfiguration configuration, string name)
        => configuration.GetConnectionString(name)
           ?? throw new InvalidOperationException($"Thiếu ConnectionStrings:{name} trong appsettings.json");
}
