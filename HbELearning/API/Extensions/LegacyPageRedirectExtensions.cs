// File: API/Extensions/LegacyPageRedirectExtensions.cs

namespace HbELearning.API.Extensions;

/// <summary>
/// Chuyển hướng địa chỉ trang KIỂU CŨ (/pages/x.html, /login.html — trước khi mỗi trang có thư mục riêng
/// trong wwwroot/pages/) sang địa chỉ mới. Không được gỡ: các địa chỉ cũ vẫn còn sống ở ngoài mã nguồn —
/// mã QR đã in trên chứng nhận (/pages/verify.html?code=...), dấu trang và lối tắt PWA đã cài trên máy người
/// dùng, đường dẫn SCO của khóa học SCORM lưu trong database (/pages/sco-lesson.html?lessonId=...).
///
/// 302 chứ không phải 301: trình duyệt nhớ 301 vĩnh viễn, lỡ phải đổi lại cấu trúc thư mục thì máy người dùng
/// vẫn bị đẩy tới địa chỉ cũ. Chuỗi truy vấn (?id=...) được giữ nguyên.
///
/// Trang MỚI không cần khai báo ở đây — bảng này chỉ dành cho địa chỉ đã từng phát hành.
/// </summary>
public static class LegacyPageRedirectExtensions
{
    private static readonly Dictionary<string, string> LegacyPathMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/login.html"] = "/pages/login/login.html",
        ["/pages/dashboard.html"] = "/pages/dashboard/dashboard.html",
        ["/pages/my-course.html"] = "/pages/my-course/my-course.html",
        ["/pages/ai-tutor.html"] = "/pages/ai-tutor/ai-tutor.html",
        ["/pages/certificate.html"] = "/pages/certificate/certificate.html",
        ["/pages/verify.html"] = "/pages/certificate/verify.html",
        ["/pages/ranking.html"] = "/pages/ranking/ranking.html",
        ["/pages/shop.html"] = "/pages/shop/shop.html",
        ["/pages/online-class.html"] = "/pages/online-class/online-class.html",
        ["/pages/classroom.html"] = "/pages/online-class/classroom.html",
        ["/pages/classroom-recap.html"] = "/pages/online-class/classroom-recap.html",
        ["/pages/course.html"] = "/pages/course/course.html",
        ["/pages/course-detail.html"] = "/pages/course/course-detail.html",
        ["/pages/approval-workflow.html"] = "/pages/approval-workflow/approval-workflow.html",
        ["/pages/sim-library.html"] = "/pages/sim-library/sim-library.html",
        ["/pages/report.html"] = "/pages/report/report.html",
        ["/pages/user.html"] = "/pages/user/user.html",
        ["/pages/organization.html"] = "/pages/organization/organization.html",
        ["/pages/role.html"] = "/pages/role/role.html",
        ["/pages/translation.html"] = "/pages/translation/translation.html",
        ["/pages/ai-document.html"] = "/pages/ai-document/ai-document.html",
        ["/pages/account.html"] = "/pages/account/account.html",
        ["/pages/learn.html"] = "/pages/learning/learn.html",
        ["/pages/scorm-player.html"] = "/pages/learning/scorm-player.html",
        ["/pages/quiz.html"] = "/pages/learning/quiz.html",
        ["/pages/sco-lesson.html"] = "/pages/learning/sco-lesson.html",
        ["/pages/sco-quiz.html"] = "/pages/learning/sco-quiz.html",
        ["/pages/simulation.html"] = "/pages/learning/simulation.html",
    };

    /// <summary>Phải đứng TRƯỚC UseDefaultFiles/UseStaticFiles — tệp cũ không còn trên đĩa nên để lọt xuống là 404.</summary>
    public static IApplicationBuilder UseLegacyPageRedirect(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && LegacyPathMap.TryGetValue(request.Path.Value ?? string.Empty, out var target))
            {
                context.Response.Redirect(target + request.QueryString.Value);
                return;
            }
            await next();
        });
}
