// File: API/Extensions/ApiServiceExtensions.cs
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace HbELearning.API.Extensions;

/// <summary>
/// Đăng ký những gì thuộc về riêng tầng trình bày: MVC, Swagger, xác thực JWT, SignalR, CORS, giới hạn
/// dung lượng tải lên. Nghiệp vụ và hạ tầng nằm ở AddApplication() / AddInfrastructure().
/// </summary>
public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddHttpContextAccessor();

        // Nghiệp vụ hỏi "ai đang thao tác" qua cổng ICurrentUser; tầng web trả lời bằng claim của thẻ JWT.
        services.AddScoped<ICurrentUser, CurrentUser>();

        // ---------- Xác thực JWT ----------
        var jwtSection = configuration.GetSection("Jwt");
        var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key");
        GuardJwtKey(jwtKey);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = jwtSection["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    // SignalR không gửi được header Authorization qua WebSocket -> nhận token qua query string "access_token".
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    },

                    // Thẻ JWT sống 8 tiếng và KHÔNG thu hồi được — chữ ký vẫn hợp lệ kể cả sau khi Admin vô
                    // hiệu hóa hay khóa tài khoản đó. Không có bước này thì một người vừa bị cho nghỉ việc,
                    // vừa bị khóa vì gõ sai mật khẩu, hay vừa bị hạ quyền vẫn dùng tiếp được hệ thống tới
                    // hết ca — đúng lúc cần chặn nhất.
                    OnTokenValidated = OnTokenValidatedAsync
                };
            });

        services.AddAuthorization();

        // Cho phép quyền do Admin cấu hình ở màn hình "Vai trò" thỏa mãn các [Authorize(Roles = ...)] gắn cứng —
        // chỉ áp dụng cho vai trò do Admin tự tạo, sáu vai trò cài sẵn giữ nguyên hành vi cũ.
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Bộ đệm cho từ điển dịch: mỗi lần tải trang của mỗi người dùng đều đọc bảng dịch, mà nội dung gần như
        // không đổi — xem TranslationController.GetDictionary.
        services.AddMemoryCache();

        // Nén phản hồi: HTML/JS/CSS/JSON thường co lại 70-85%, giảm mạnh thời gian tải trên mạng nội bộ chậm
        // hoặc Wi-Fi yếu. Nhanh nhất (Fastest) để không đổi CPU lấy băng thông — mạng công ty thường dư băng thông.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
                .Concat(["image/svg+xml", "application/manifest+json", "model/gltf+json"]);
        });
        services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
            o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
        services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
            o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

        services.AddSignalR();
        // Nghiệp vụ chat báo "có tin mới" qua cổng IChatNotifier; tầng web đẩy đi bằng SignalR.
        services.AddScoped<IChatNotifier, SignalRChatNotifier>();

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
        });

        // ---------- CORS ----------
        // Giao diện nằm CÙNG máy chủ với API (wwwroot), nên đường đi bình thường không cần CORS chút nào.
        // Mở AllowAnyOrigin như trước là cho phép bất kỳ trang web nào trên Internet gọi API này bằng trình
        // duyệt của nhân viên và ĐỌC ĐƯỢC phản hồi — chỉ cần thẻ JWT lọt ra ngoài một lần là khai thác được
        // từ một tên miền lạ. Muốn cho một giao diện khác miền gọi vào thì liệt kê tên miền đó ở
        // "Cors:AllowedOrigins" trong appsettings, không mở toang lại.
        var allowedOriginList = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (allowedOriginList.Length == 0)
                {
                    // Không khai báo gì = chỉ dùng chung một tên miền với giao diện. Trang cùng tên miền
                    // KHÔNG đi qua CORS nên vẫn chạy bình thường; trang khác tên miền bị từ chối.
                    policy.SetIsOriginAllowed(_ => false);
                    return;
                }

                policy.WithOrigins(allowedOriginList).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            });
        });

        // ---------- Giới hạn tần suất ----------
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Đăng nhập / quên mật khẩu / nhập OTP: đếm theo địa chỉ IP. Khóa tài khoản sau 3 lần sai chỉ
            // chặn được việc dò mật khẩu của MỘT người; nó không chặn được kiểu dò ngược (một mật khẩu phổ
            // biến thử lần lượt qua hàng trăm tài khoản) và cũng không chặn được việc bấm "Quên mật khẩu"
            // hàng nghìn lần để đốt tiền SMS/ZNS của công ty.
            options.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Tra cứu chứng nhận mở cho người ngoài nên là chỗ dễ bị quét hàng loạt nhất.
            options.AddPolicy(PublicRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });


        return services;
    }

    /// <summary>Tên chính sách CORS — dùng chung giữa nơi đăng ký và nơi lắp vào đường ống.</summary>
    public const string CorsPolicyName = "HbELearningCors";

    /// <summary>Chính sách giới hạn tần suất cho các điểm cuối đăng nhập / quên mật khẩu / OTP.</summary>
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>Chính sách giới hạn tần suất cho các điểm cuối mở công khai (không cần đăng nhập).</summary>
    public const string PublicRateLimitPolicy = "public";

    /// <summary>Khóa phân loại của bộ đếm tần suất: địa chỉ IP người gọi. Đứng sau reverse proxy thì phải bật
    /// ForwardedHeaders ở proxy, không thì mọi người dùng chung một khóa.</summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Chặn ngay từ cửa những khóa ký JWT không an toàn.
    ///
    /// Khóa ký là thứ quyết định "ai là ai" trong toàn hệ thống: biết khóa là tự ký được một thẻ mang vai trò
    /// Admin, không cần mật khẩu của ai cả. Giá trị mẫu đi kèm mã nguồn vì thế phải coi như đã lộ.
    ///
    /// Chỉ ném lỗi khi chạy thật, còn môi trường phát triển thì cho qua để không chặn việc chạy thử.
    /// </summary>
    private static void GuardJwtKey(string key)
    {
        var isProduction = !string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
        if (!isProduction) return;

        const string sampleKey = "HbELearning_2026_SuperSecretKey_ChangeMe_MinimumLength32Bytes!";
        if (key == sampleKey || key.Contains("ChangeMe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Jwt:Key vẫn đang là khóa mẫu đi kèm mã nguồn. Ai đọc được mã nguồn cũng tự ký được thẻ đăng "
                + "nhập mang quyền Admin. Hãy đặt một khóa ngẫu nhiên riêng qua biến môi trường Jwt__Key "
                + "(hoặc dotnet user-secrets) trước khi chạy thật.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key phải dài tối thiểu 32 byte cho thuật toán HMAC-SHA256.");
    }

    /// <summary>
    /// Sau khi chữ ký của thẻ JWT đã hợp lệ, hỏi lại database xem tài khoản đó CÒN được vào hay không.
    ///
    /// Có bộ đệm 60 giây để không thành một lượt truy vấn cho mỗi yêu cầu HTTP. Trong vòng một phút sau khi
    /// khóa tài khoản, người đó vẫn thao tác được — đổi lại hệ thống không phải gánh thêm một lượt đọc
    /// database trên từng lần bấm chuột. Với mục đích "cắt quyền người vừa nghỉ việc" thì một phút là đủ.
    /// </summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var userIdText = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdText, out var userId) || userId <= 0)
        {
            context.Fail("Thẻ đăng nhập không hợp lệ.");
            return;
        }

        // Vé SignalR đi trong địa chỉ nên dễ lọt vào nhật ký. Chặn nó ở mọi đường KHÁC /hubs: nhặt được
        // một vé cũng không gọi được API nào, và chỉ còn hai phút để kịp dùng.
        var tokenUse = context.Principal?.FindFirst(AppClaimType.TokenUseClaimType)?.Value;
        if (tokenUse == AppClaimType.HubTokenUse
            && !context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Fail("Vé này chỉ dùng được cho kênh thời gian thực.");
            return;
        }

        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        var cacheKey = $"auth:active:{userId}";
        if (cache.TryGetValue<bool>(cacheKey, out var cachedIsUsable))
        {
            if (!cachedIsUsable) context.Fail("Tài khoản đã bị khóa hoặc vô hiệu hóa.");
            return;
        }

        bool isUsable;
        try
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
            isUsable = await db.User.AsNoTracking()
                .AnyAsync(x => x.Id == userId && x.IsActive && x.LockedOutAt == null);
        }
        catch
        {
            // Database trục trặc thì KHÔNG đăng xuất cả công ty: để thẻ đi tiếp như trước khi có bước này.
            // Mọi thao tác thật phía sau đều cần database, nên cũng không mở thêm cửa nào.
            return;
        }

        cache.Set(cacheKey, isUsable, TimeSpan.FromSeconds(60));
        if (!isUsable) context.Fail("Tài khoản đã bị khóa hoặc vô hiệu hóa.");
    }
}
