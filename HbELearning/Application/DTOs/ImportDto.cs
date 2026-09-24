// File: Application/DTOs/ImportDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Kết quả nhập liệu hàng loạt từ file Excel.</summary>
public record ImportResultDto(
    int TotalRow, int SuccessCount, int FailCount,
    IReadOnlyList<ImportErrorDto> ErrorList)
{
    public string Message => FailCount == 0
        ? $"Đã nhập thành công {SuccessCount}/{TotalRow} dòng."
        : $"Nhập được {SuccessCount}/{TotalRow} dòng. {FailCount} dòng bị bỏ qua do lỗi dữ liệu.";
}

public record ImportErrorDto(int RowNumber, string Message);

/// <summary>Xem trước việc nhập MỘT file Word ngân hàng câu hỏi gộp nhiều bài — chưa tạo gì cả.</summary>
/// <param name="WarningList">Câu hỏi đọc không được (thiếu đáp án đúng, dưới 2 đáp án...), kèm số dòng.</param>
public record QuestionOutlineDto(
    IReadOnlyList<QuestionSectionDto> SectionList, IReadOnlyList<ImportErrorDto> WarningList);

/// <param name="Index">Số thứ tự nhóm trong file — gửi lại đúng số này khi chọn nhóm muốn nhập.</param>
/// <param name="TargetQuizTitle">Bài kiểm tra sẽ nhận số câu hỏi này.</param>
/// <param name="IsNewQuiz">true = chưa có bài kiểm tra nào khớp tên, hệ thống sẽ tạo mới.</param>
/// <param name="LessonTitle">Bài học mà bài kiểm tra mới sẽ gắn vào, null nếu là bài kiểm tra độc lập.</param>
/// <param name="ReviewCount">Số câu đọc được nhưng chưa xác định đáp án đúng — vẫn nhập, người soạn tick lại sau.</param>
public record QuestionSectionDto(
    int Index, string Title, int QuestionCount, string TargetQuizTitle, bool IsNewQuiz, string? LessonTitle,
    int ReviewCount);

/// <summary>Kết quả nhập file Word ngân hàng câu hỏi. Dùng lại đúng bộ tên trường của
/// <see cref="ImportResultDto"/> để giao diện hiện kết quả bằng chung một khối hiển thị.</summary>
public record QuestionDocumentResultDto(
    int TotalRow, int SuccessCount, int FailCount, IReadOnlyList<ImportErrorDto> ErrorList,
    int QuizCreatedCount, int QuizUsedCount, int ReviewCount = 0)
{
    public string Message => (FailCount == 0
            ? $"Đã nhập {SuccessCount} câu hỏi"
            : $"Nhập được {SuccessCount}/{TotalRow} câu hỏi, {FailCount} câu bị bỏ qua")
        + $" vào {QuizUsedCount} bài kiểm tra"
        + (QuizCreatedCount > 0 ? $" (tạo mới {QuizCreatedCount} bài)" : "")
        + (ReviewCount > 0
            ? $". Có {ReviewCount} câu chưa xác định được đáp án đúng — hãy mở ngân hàng câu hỏi và tick lại."
            : ".");
}
