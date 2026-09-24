// File: API/Extensions/ApiServiceExtensions.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
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

                // SignalR không gửi được header Authorization qua WebSocket -> nhận token qua query string "access_token".
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        // Cho phép quyền do Admin cấu hình ở màn hình "Vai trò" thỏa mãn các [Authorize(Roles = ...)] gắn cứng —
        // chỉ áp dụng cho vai trò do Admin tự tạo, sáu vai trò cài sẵn giữ nguyên hành vi cũ.
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Bộ đệm cho từ điển dịch: mỗi lần tải trang của mỗi người dùng đều đọc bảng dịch, mà nội dung gần như
        // không đổi — xem TranslationController.GetDictionary.
        services.AddMemoryCache();

        services.AddSignalR();
        // Nghiệp vụ chat báo "có tin mới" qua cổng IChatNotifier; tầng web đẩy đi bằng SignalR.
        services.AddScoped<IChatNotifier, SignalRChatNotifier>();

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
        });

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });


        return services;
    }
}
