// File: Application/Services/IDataImportService.cs

namespace HbELearning.Application.Services;

/// <summary>Nhập dữ liệu hàng loạt và sinh file mẫu — xem DataImportService.</summary>
public interface IDataImportService
{
    /// <summary>File mẫu nhập câu hỏi. Excel và Word dùng CHUNG bộ cột, dòng ví dụ và phần hướng dẫn
    /// (xem QuestionTemplate) — chỉ khác vỏ đựng, nên sửa một chỗ là cả hai định dạng đổi theo.</summary>
    byte[] BuildQuestionTemplate(ImportFormat format = ImportFormat.Excel);
    /// <summary>File mẫu nhập câu hỏi cho cả khóa học: cùng bộ cột với file mẫu trên, thêm cột "Bài kiểm tra"
    /// ở đầu để một file chứa câu hỏi của nhiều bài.</summary>
    byte[] BuildQuestionSheetTemplate(ImportFormat format = ImportFormat.Excel);

    byte[] BuildLessonTemplate(ImportFormat format = ImportFormat.Excel);
    byte[] BuildUserTemplate();
    byte[] BuildDepartmentTemplate();
    byte[] BuildPositionTemplate();
    Task<ImportResultDto> ImportQuestionAsync(int quizId, Stream stream, ImportFormat format);

    /// <summary>Đọc bảng câu hỏi cả khóa học thành các nhóm theo cột "Bài kiểm tra" — KHÔNG ghi gì vào
    /// database, phần ghép nhóm vào bài kiểm tra nằm ở IImportService.</summary>
    QuestionSheet ReadQuestionSheet(Stream stream, ImportFormat format);
    Task<ImportResultDto> ImportLessonAsync(int courseId, Stream stream, ImportFormat format);
    Task<ImportResultDto> ImportUserAsync(Stream stream);
    Task<ImportResultDto> ImportDepartmentAsync(Stream stream);
    Task<ImportResultDto> ImportPositionAsync(Stream stream);
}
