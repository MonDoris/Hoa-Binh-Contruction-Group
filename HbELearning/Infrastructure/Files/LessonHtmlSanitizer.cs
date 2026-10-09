// File: Infrastructure/Files/LessonHtmlSanitizer.cs
// Đặt bí danh để không lẫn với IHtmlSanitizer của chính hệ thống (cổng ở lớp Application): hai cái trùng
// tên, mà lớp này phải cài đặt cái của hệ thống và DÙNG cái của thư viện.
using GanssSanitizer = Ganss.Xss.HtmlSanitizer;

namespace HbELearning.Infrastructure.Files;

/// <summary>
/// Hiện thực <see cref="IHtmlSanitizer"/> bằng thư viện HtmlSanitizer (phân tích HTML thật bằng AngleSharp,
/// không phải cắt bằng biểu thức chính quy — cắt bằng regex thì luôn còn một cách viết lách qua được).
///
/// Danh sách cho phép được NỚI RỘNG so với mặc định của thư viện, vì nội dung ở đây không phải mấy dòng
/// bình luận mà là tài liệu đào tạo dựng lại từ Word/PowerPoint: phải giữ nguyên bảng gộp ô, độ rộng cột,
/// phông chữ, màu nền, ảnh chèn trong bài. Mất những thứ đó thì tính năng "nhập nguyên tài liệu" coi như hỏng.
///
/// Thứ bị cắt là thứ CHẠY ĐƯỢC hoặc GỌI RA NGOÀI:
///   • thẻ script, iframe, object, embed, form, input...;
///   • mọi thuộc tính sự kiện (onclick, onerror, onload...);
///   • liên kết dùng lược đồ javascript: / data: / vbscript:;
///   • thuộc tính style chứa url(...) hay expression(...).
/// </summary>
public sealed class LessonHtmlSanitizer : Application.Common.Interfaces.IHtmlSanitizer
{
    /// <summary>Dựng một lần rồi dùng lại: bộ lọc không giữ trạng thái giữa các lần gọi, mà việc dựng nó có
    /// kèm phân tích cấu hình nên không nên lặp lại trên từng lần lưu bài.</summary>
    private static readonly GanssSanitizer Sanitizer = Build();

    public string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;
        return Sanitizer.Sanitize(html);
    }

    private static GanssSanitizer Build()
    {
        var sanitizer = new GanssSanitizer();

        // ---------- Thẻ ----------
        // Mặc định của thư viện đã có sẵn phần lớn thẻ trình bày. Bổ sung những thẻ mà bộ chuyển Word /
        // PowerPoint của hệ thống sinh ra.
        foreach (var tag in new[] { "figure", "figcaption", "picture", "source", "mark", "small", "section", "article" })
            sanitizer.AllowedTags.Add(tag);

        // Thẻ nhúng nội dung bên ngoài: bỏ hẳn. Bài học cần video thì đã có trường "Đường dẫn tài nguyên"
        // riêng, đi qua trình phát của hệ thống — không cần người soạn tự dán mã nhúng vào nội dung.
        foreach (var tag in new[] { "iframe", "frame", "frameset", "object", "embed", "applet",
                                    "script", "style", "form", "input", "button", "textarea", "select" })
            sanitizer.AllowedTags.Remove(tag);

        // ---------- Thuộc tính ----------
        // Giữ những thuộc tính dựng lại bố cục bảng của Word.
        foreach (var attribute in new[] { "style", "class", "id", "colspan", "rowspan", "width", "height",
                                          "align", "valign", "border", "cellpadding", "cellspacing",
                                          "src", "alt", "title", "href", "target", "rel", "data-page" })
            sanitizer.AllowedAttributes.Add(attribute);

        // ---------- CSS ----------
        // Danh sách mặc định của thư viện theo chuẩn CSS phổ thông, thiếu vài thuộc tính mà bộ chuyển
        // PowerPoint của hệ thống dùng để dựng lại hình khối và tỉ lệ khung slide. Không thêm vào đây thì
        // slide nhập từ PowerPoint mất hình cắt và méo tỉ lệ — đã đo bằng CssCheck trong bộ kiểm.
        // Cả ba đều thuần trình bày, không tải được gì từ bên ngoài.
        foreach (var property in new[] { "clip-path", "aspect-ratio", "stroke", "stroke-width", "fill", "object-fit", "gap" })
            sanitizer.AllowedCssProperties.Add(property);

        // Giữ gần như toàn bộ thuộc tính trình bày mà bộ chuyển Word sinh ra; riêng những thứ có thể tải
        // nội dung từ ngoài hoặc chạy mã thì bỏ.
        foreach (var property in new[] { "behavior", "expression", "binding", "-moz-binding", "filter" })
            sanitizer.AllowedCssProperties.Remove(property);

        // ---------- Lược đồ liên kết ----------
        // Chỉ http/https/mailto. KHÔNG có javascript: (chạy mã khi bấm) và KHÔNG có data: (một liên kết
        // data:text/html mở ra là một trang HTML do người soạn tự viết, chạy trong chính tên miền này).
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" }) sanitizer.AllowedSchemes.Add(scheme);

        // Bỏ luôn thẻ chuyển hướng ngầm và thẻ đặt đường dẫn gốc.
        foreach (var tag in new[] { "meta", "base", "link" }) sanitizer.AllowedTags.Remove(tag);

        // ---------- Tài nguyên tải tự động từ máy chủ bên ngoài ----------
        // Một thẻ <img> hay một background:url() trỏ ra ngoài sẽ được trình duyệt tải NGẦM ngay khi mở bài
        // học — không cần ai bấm gì. Chủ máy chủ đó nhận được địa chỉ IP, thời điểm và trình duyệt của từng
        // học viên: đủ để đếm ai đã mở tài liệu nội bộ nào, lúc nào. Ảnh của hệ thống luôn nằm trong
        // /uploads (bộ chuyển Word/PowerPoint tự lưu về), nên chặn đường ra ngoài không mất gì cả.
        //
        // Ngoại lệ là thẻ <a>: đó là liên kết người học TỰ BẤM và tự thấy mình đang đi đâu, khác hẳn một
        // tài nguyên tải ngầm.
        sanitizer.FilterUrl += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.SanitizedUrl)) return;

            var tagName = e.Tag?.TagName;
            if (string.Equals(tagName, "A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "AREA", StringComparison.OrdinalIgnoreCase)) return;

            // "//host/x" và "https://host/x" là ra ngoài; "/uploads/..." và "anh.png" là của chính hệ thống.
            var url = e.SanitizedUrl.TrimStart();
            if (url.StartsWith("//", StringComparison.Ordinal) || url.Contains("://", StringComparison.Ordinal))
                e.SanitizedUrl = null;
        };

        return sanitizer;
    }
}
