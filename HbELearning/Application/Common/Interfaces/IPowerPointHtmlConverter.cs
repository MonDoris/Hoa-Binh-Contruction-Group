// File: Application/Common/Interfaces/IPowerPointHtmlConverter.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Chuyển bài giảng PowerPoint (.pptx) thành HTML: mỗi slide là một khung đúng tỉ lệ của bản gốc, chữ và
/// hình nằm đúng vị trí, co giãn theo bề ngang màn hình. Học viên đọc thẳng trong trang học, không phải tải
/// file về rồi mở bằng ứng dụng khác.
///
/// Giữ được: kích thước slide, nền slide (màu, dải màu, ảnh), hình trang trí và logo của slide master /
/// layout, vị trí và cỡ của từng hộp chữ (kể cả placeholder kế thừa vị trí từ layout), phông / cỡ / màu /
/// đậm / nghiêng / gạch chân của chữ theo đúng chuỗi kế thừa của PowerPoint, gạch đầu dòng và đánh số, canh
/// lề, giãn dòng, tự thu nhỏ chữ, hình khối cơ bản có màu nền và viền, ảnh (kể cả cắt ảnh), nhóm hình, đường
/// thẳng, bảng, siêu liên kết, ghi chú của người trình bày.
///
/// KHÔNG giữ: hiệu ứng chuyển slide và animation, biểu đồ, SmartArt, video/âm thanh nhúng (chỉ còn ảnh
/// bìa), hình vẽ tự do (custGeom vẽ thành hình chữ nhật), đổ bóng và hiệu ứng 3D, màu lấy từ kiểu bảng tùy
/// biến (bảng dùng một kiểu màu mặc định theo màu chủ đạo của theme).
/// </summary>
public interface IPowerPointHtmlConverter
{
    /// <summary>Đọc .pptx thành HTML. <paramref name="saveImageAsync"/> nhận (dữ liệu ảnh, phần mở rộng) và
    /// trả về đường dẫn để nhúng vào thẻ img — để lớp gọi tự quyết định lưu ảnh ở đâu.</summary>
    Task<PowerPointHtmlResult> ConvertAsync(Stream pptxStream, Func<byte[], string, Task<string>> saveImageAsync);
}
