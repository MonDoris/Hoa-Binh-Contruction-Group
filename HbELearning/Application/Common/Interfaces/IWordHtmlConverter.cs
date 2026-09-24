// File: Application/Common/Interfaces/IWordHtmlConverter.cs
// Cong (port) tach ra tu Infrastructure/Documents/WordHtmlConverter.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Chuyển file Word (.docx) thành HTML giữ nguyên cách trình bày, để một tài liệu đào tạo có sẵn nhập vào
/// hệ thống là hiện ra gần như y hệt bản gốc chứ không rớt xuống thành một khối chữ trơn.
///
/// Giữ được: định dạng khai báo trong style của tài liệu (thứ chiếm phần lớn định dạng của một file Word
/// thật), cấp tiêu đề, phông chữ, cỡ chữ, màu chữ, màu bôi nền, in đậm/nghiêng/gạch chân/gạch ngang, chỉ số
/// trên và dưới, canh lề, thụt lề, giãn dòng và giãn đoạn, khung viền và nền của đoạn, danh sách nhiều cấp
/// đúng kiểu ký hiệu, bảng có gộp ô cả ngang lẫn dọc kèm đường kẻ và độ rộng cột, bảng lồng trong bảng,
/// hộp văn bản, hình ảnh đúng kích thước bản gốc, siêu liên kết và dấu trang, ngắt trang.
///
/// KHÔNG giữ: đánh số trang, header/footer, biểu đồ, SmartArt, công thức toán, chú thích cuối trang, hiệu
/// ứng chữ nghệ thuật. Đây là những thứ chỉ có nghĩa trên trang giấy A4 hoặc cần một bộ dựng hình riêng,
/// còn bài học thì hiển thị trong một khung web co giãn.
/// </summary>
public interface IWordHtmlConverter
{
    /// <summary>Đọc .docx thành HTML. <paramref name="saveImageAsync"/> nhận (dữ liệu ảnh, phần mở rộng) và
    /// trả về đường dẫn để nhúng vào thẻ img — để lớp gọi tự quyết định lưu ảnh ở đâu.</summary>
    Task<WordHtmlResult> ConvertAsync(Stream docxStream, Func<byte[], string, Task<string>> saveImageAsync);

    /// <summary>
    /// Đọc .docx rồi TÁCH thành nhiều phần — một giáo trình gộp nhiều bài trong cùng một file Word nhập vào
    /// thành nhiều bài học riêng, thay vì một bài dài.
    ///
    /// Tự nhận ra chỗ sang bài mới, theo thứ tự ưu tiên:
    ///   1. Cấp tiêu đề (Heading) nào mà PHẦN LỚN các dòng của nó mở đầu bằng "Bài 1", "Chương 2", "Phần 3",
    ///      "Chuyên đề 4" — đây gần như chắc chắn là cấp chia bài, kể cả khi nó không phải Heading 1.
    ///   2. Không có cấp nào như vậy thì lấy chính các dòng mang mẫu chữ trên, dù người soạn gõ chữ thường
    ///      không đặt Heading.
    ///   3. Vẫn không có thì lấy cấp tiêu đề cao nhất xuất hiện từ hai lần trở lên.
    /// Không tìm được dấu hiệu nào thì trả về đúng một phần — cả tài liệu là một bài học.
    ///
    /// <paramref name="saveImageAsync"/> trả về chuỗi rỗng thì ảnh bị bỏ qua — dùng khi chỉ cần xem trước
    /// danh sách phần, chưa muốn ghi ảnh ra đĩa.
    /// </summary>
    Task<WordSectionResult> ConvertSectionsAsync(Stream docxStream, Func<byte[], string, Task<string>> saveImageAsync);
}
