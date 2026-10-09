// File: Application/Common/Interfaces/IHtmlSanitizer.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Lọc HTML do người dùng đưa vào trước khi lưu, bỏ những thứ chạy được mã.
///
/// VÌ SAO CẦN: nội dung bài học được lưu nguyên dạng HTML rồi đổ thẳng vào trang học bằng innerHTML
/// (xem renderLessonBody trong wwwroot/pages/learn.html) — đó là cách duy nhất giữ được bảng, ảnh và định
/// dạng của tài liệu nhập từ Word. Nhưng nó cũng có nghĩa: một thẻ &lt;script&gt; lọt vào ô nội dung sẽ
/// CHẠY trên máy của mọi học viên và mọi quản trị viên mở bài đó, đọc được thẻ đăng nhập họ đang giữ.
///
/// Người soạn nội dung là người trong công ty, nhưng đường vào không chỉ có ô soạn thảo: cột "Nội dung
/// (HTML)" của file mẫu Excel nhận HTML thô, nên chỉ cần một người soạn tải lên file ai đó gửi cho là đủ.
///
/// LỌC CHỨ KHÔNG MÃ HÓA: mã hóa toàn bộ thì bài học nhập từ Word mất sạch bảng biểu và trở thành một khối
/// chữ có đầy dấu &amp;lt;. Bộ lọc giữ lại thẻ trình bày, chỉ cắt phần chạy được mã.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>Trả về đúng chuỗi HTML đó sau khi đã cắt bỏ script, sự kiện on*, liên kết javascript: và
    /// các thẻ nhúng nội dung bên ngoài. null/rỗng vào thì null/rỗng ra.</summary>
    string? Sanitize(string? html);
}
