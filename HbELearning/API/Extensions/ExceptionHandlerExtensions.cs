// File: API/Extensions/ExceptionHandlerExtensions.cs
using Microsoft.AspNetCore.Diagnostics;

namespace HbELearning.API.Extensions;

/// <summary>
/// Lưới hứng cuối cùng cho mọi lỗi không ai bắt: ghi đầy đủ vào nhật ký máy chủ, nhưng chỉ trả ra ngoài một
/// câu chung chung kèm mã tra cứu.
///
/// VÌ SAO: thông điệp của một ngoại lệ thường mang theo thứ chỉ máy chủ mới được biết — đường dẫn tuyệt đối
/// trên ổ đĩa, tên máy chủ database, tên bảng và tên cột, chuỗi kết nối trong lỗi mạng. Ghép mấy mẩu đó lại
/// là có bản đồ hệ thống để chuẩn bị một đợt tấn công tiếp theo.
///
/// Mã tra cứu (traceId) để người dùng đọc cho bộ phận kỹ thuật: tra trong nhật ký ra đúng lỗi đó, không cần
/// hiện nguyên văn lỗi lên màn hình của họ.
/// </summary>
public static class ExceptionHandlerExtensions
{
    public static IApplicationBuilder UseSafeExceptionHandler(this IApplicationBuilder app) =>
        app.UseExceptionHandler(builder => builder.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");

            var traceId = context.TraceIdentifier;
            logger.LogError(feature?.Error,
                "Loi khong bat duoc khi xu ly {Method} {Path} (traceId {TraceId})",
                context.Request.Method, context.Request.Path, traceId);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            // Đúng hình dạng MessageResponse để giao diện đọc được data.message như mọi lỗi khác (xem js/api.js).
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Hệ thống gặp lỗi khi xử lý yêu cầu này. Vui lòng thử lại; "
                          + $"nếu vẫn lỗi, báo bộ phận kỹ thuật kèm mã {traceId}."
            });
        }));
}
