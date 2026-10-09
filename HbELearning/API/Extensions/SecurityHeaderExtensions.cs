// File: API/Extensions/SecurityHeaderExtensions.cs

namespace HbELearning.API.Extensions;

/// <summary>
/// Các đầu mục bảo mật gắn vào MỌI phản hồi. Đây là hàng rào của trình duyệt, không phải của máy chủ: chúng
/// không chặn được kẻ tấn công gọi thẳng API, nhưng chặn được những cách lợi dụng đi qua trình duyệt của
/// chính người dùng thật — nhúng trang vào iframe để lừa bấm, đoán kiểu tệp để chạy mã, rò địa chỉ trang nội
/// bộ sang site khác qua Referer.
///
/// CỐ Ý CHƯA đặt Content-Security-Policy: giao diện hiện tại viết rất nhiều &lt;script&gt; và style ngay trong
/// HTML, bật CSP nghiêm (không có 'unsafe-inline') là trắng trang toàn hệ thống. Muốn bật thì phải tách
/// script/style ra tệp riêng hoặc gắn nonce trước — xem phần khuyến nghị trong báo cáo rà soát bảo mật.
/// </summary>
public static class SecurityHeaderExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Trình duyệt phải theo đúng Content-Type máy chủ khai, không được tự đoán. Không có dòng này thì
            // một tệp người dùng tải lên có nội dung giống HTML vẫn có thể bị chạy như HTML.
            headers["X-Content-Type-Options"] = "nosniff";

            // Không cho SITE KHÁC nhúng trang này vào iframe (clickjacking: phủ một nút vô hình lên nút thật).
            //
            // SAMEORIGIN chứ không phải DENY: bài học dạng tài liệu tự nhúng tệp PDF của chính hệ thống bằng
            // <iframe src="/uploads/..."> (xem learn.html), mà DENY thì chặn cả trường hợp trang tự nhúng
            // chính mình — người học sẽ thấy một khung trắng thay cho tài liệu.
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Content-Security-Policy"] = "frame-ancestors 'self'";

            // Sang site khác thì chỉ gửi tên miền, không gửi kèm đường dẫn — đường dẫn nội bộ hay chứa id
            // khóa học, id người dùng và cả url tệp đính kèm.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Không trang nào của hệ thống cần mấy quyền này; tắt sẵn để một đoạn mã lạ chèn được vào trang
            // cũng không bật trộm được camera/micro. Riêng phòng học online tự xin quyền qua getUserMedia
            // trên CHÍNH trang này nên "self" vẫn phải giữ.
            headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=(), payment=()";

            await next();
        });
}
