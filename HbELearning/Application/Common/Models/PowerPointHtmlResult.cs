// File: Application/Common/Models/PowerPointHtmlResult.cs
// Kiểu kết quả của cổng IPowerPointHtmlConverter.

namespace HbELearning.Application.Common.Models;

/// <param name="Html">Nội dung HTML của cả bài giảng (rỗng nếu không có slide nào hiện được).</param>
/// <param name="Title">Tiêu đề của slide đầu tiên, null nếu slide đó không có ô tiêu đề.</param>
/// <param name="SlideCount">Số slide đã dựng (slide bị ẩn trong PowerPoint không tính).</param>
/// <param name="ImageCount">Số ảnh đã trích ra và lưu lại (ảnh dùng lại ở nhiều slide chỉ tính một lần).</param>
/// <param name="SkippedObjectCount">Số đối tượng không dựng được (biểu đồ, SmartArt, ảnh định dạng lạ...).</param>
public record PowerPointHtmlResult(string Html, string? Title, int SlideCount, int ImageCount, int SkippedObjectCount);
