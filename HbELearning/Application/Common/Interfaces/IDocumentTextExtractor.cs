// File: Application/Common/Interfaces/IDocumentTextExtractor.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Bóc tách chữ thô từ một tài liệu đã lưu trên đĩa.
///
/// Đọc PDF (PdfPig) hay Word (OpenXml) là chi tiết thư viện; nghiệp vụ kho tri thức chỉ cần "chữ trong tài liệu
/// này là gì" để cắt đoạn và nhúng vector.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Chữ thô của tài liệu, CHƯA gộp khoảng trắng.</summary>
    /// <param name="filePath">Đường dẫn tệp trên đĩa.</param>
    /// <param name="extension">Phần mở rộng đã chuẩn hóa, chữ thường, không dấu chấm (VD "pdf").</param>
    /// <exception cref="FileNotFoundException">Tệp không tồn tại.</exception>
    /// <exception cref="NotSupportedException">Định dạng chưa hỗ trợ.</exception>
    Task<string> ExtractTextAsync(string filePath, string extension, CancellationToken cancellationToken);
}
