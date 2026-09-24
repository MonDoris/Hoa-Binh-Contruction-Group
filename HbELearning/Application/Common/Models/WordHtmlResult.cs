// File: Application/Common/Models/WordHtmlResult.cs
// Kiểu kết quả của cổng IWordHtmlConverter, tách ra từ Infrastructure/Documents/WordHtmlConverter.cs.

namespace HbELearning.Application.Common.Models;

/// <param name="Html">Nội dung HTML của tài liệu.</param>
/// <param name="Title">Tiêu đề đoán được từ tài liệu (dòng tiêu đề cấp cao nhất đầu tiên), null nếu không có.</param>
/// <param name="ImageCount">Số ảnh đã trích ra và lưu lại.</param>
public record WordHtmlResult(string Html, string? Title, int ImageCount);
