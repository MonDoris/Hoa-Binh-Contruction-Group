// File: Application/Services/IDocumentProcessingService.cs

namespace HbELearning.Application.Services;

public interface IDocumentProcessingService
{
    /// <summary>Đọc tài liệu (pdf/docx/txt), cắt thành đoạn, lấy vector nhúng từ Ollama và lưu vào database.
    /// Nạp lại cùng một file sẽ XÓA các đoạn cũ của file đó trước, tránh nhân đôi dữ liệu.</summary>
    Task<DocumentIngestResultDto> ProcessAndSaveDocumentAsync(
        string filePath, string? fileType = null, int? courseId = null, int? lessonId = null,
        string? sourceFileUrl = null, string? displayFileName = null, CancellationToken cancellationToken = default);

    /// <summary>Sinh sẵn vector câu hỏi vào bộ đệm để chạy song song với việc khác của người gọi; không ném lỗi.</summary>
    Task PrefetchQueryVectorAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Tìm các đoạn tài liệu gần nghĩa nhất với câu hỏi (tìm kiếm ngữ nghĩa).
    ///
    /// <paramref name="allowedCourseIdList"/> là HÀNG RÀO dữ liệu, khác hẳn <paramref name="courseId"/>:
    /// courseId là bộ lọc người dùng tự chọn để thu hẹp phạm vi tra cứu, còn danh sách này là những khóa học
    /// người gọi ĐƯỢC PHÉP đọc tài liệu. Truyền null nghĩa là không rào (dùng cho màn hình quản trị).</summary>
    Task<List<DocumentChunkMatchDto>> SearchAsync(
        string query, int topCount = 5, int? courseId = null,
        List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default);

    /// <summary>Lấy phần tài liệu làm ngữ cảnh để model sinh câu trả lời: tìm các đoạn khớp nhất rồi
    /// bù thêm đoạn liền trước/liền sau của những đoạn khớp nhất, sắp lại theo đúng thứ tự trong tài liệu.</summary>
    Task<List<DocumentChunkMatchDto>> GetAnswerContextAsync(
        string question, int topCount = 5, int? courseId = null,
        List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default);

    /// <summary>Xóa toàn bộ đoạn của một tài liệu khỏi kho tri thức. Trả về số đoạn đã xóa.</summary>
    Task<int> DeleteBySourceAsync(string sourceFileName, int? courseId = null, CancellationToken cancellationToken = default);
}
