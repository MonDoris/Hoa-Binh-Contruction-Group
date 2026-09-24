// File: Application/Common/Models/QuestionSheet.cs
// Kết quả đọc một BẢNG câu hỏi soạn theo file mẫu, loại có thêm cột "Bài kiểm tra" ở đầu.

namespace HbELearning.Application.Common.Models;

/// <summary>
/// Các nhóm câu hỏi đọc được từ một bảng (Excel / bảng trong Word / PDF) và những dòng phải bỏ qua.
///
/// Cùng hình dạng với <see cref="WordQuestionDocument"/> — nhóm theo bài, kèm danh sách chỗ hỏng — để hai lối
/// nhập (bảng theo file mẫu và file Word soạn tự do) dùng chung đúng một đoạn ghép nhóm vào bài kiểm tra và
/// một đoạn ghi database ở ImportService.
/// </summary>
/// <param name="TotalRow">Tổng số dòng dữ liệu đọc được trong file, kể cả dòng hỏng — để báo "nhập được x/y".</param>
public record QuestionSheet(
    IReadOnlyList<ParsedQuestionGroup> GroupList, IReadOnlyList<(int Line, string Message)> ErrorList, int TotalRow);
