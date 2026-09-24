// File: Application/Common/Models/WordQuestionDocument.cs
// Kết quả bóc tách một file Word ngân hàng câu hỏi soạn tự do.

namespace HbELearning.Application.Common.Models;

/// <param name="Content">Nội dung đáp án (đã bỏ chữ cái đầu dòng "A.", "B)").</param>
public record ParsedAnswer(string Content, bool IsCorrect);

/// <param name="SourceLine">Số thứ tự dòng trong tài liệu — để báo lỗi chỉ đúng chỗ cho người soạn sửa.</param>
/// <param name="NeedsAnswerReview">true = đọc được câu hỏi và các đáp án nhưng KHÔNG xác định được đáp án nào
/// đúng. Câu vẫn được nhập (không đáp án nào được đánh dấu đúng) để người soạn tick lại trong màn hình ngân
/// hàng câu hỏi — chặn lại và báo lỗi thì cả đề không vào được, sửa trong Word rồi nhập lại còn lâu hơn.</param>
/// <param name="DeclaredType">Kiểu câu hỏi do người soạn ghi rõ (cột "Kiểu câu hỏi" của file mẫu). null = không
/// ai ghi, để nơi tạo câu hỏi tự suy ra từ số đáp án đúng và nội dung đáp án — file Word soạn tự do luôn rơi
/// vào trường hợp này.</param>
public record ParsedQuestion(
    string Content, IReadOnlyList<ParsedAnswer> AnswerList, string? Explanation, decimal? Score, int SourceLine,
    bool NeedsAnswerReview = false, QuestionType? DeclaredType = null);

/// <param name="Title">Tên nhóm, lấy từ dòng "BÀI 1: ..." mở đầu nhóm. null = nhóm câu hỏi nằm trước dòng
/// chia bài đầu tiên.</param>
public record ParsedQuestionGroup(string? Title, IReadOnlyList<ParsedQuestion> QuestionList);

/// <param name="GroupList">Các nhóm theo đúng thứ tự trong tài liệu.</param>
/// <param name="WarningList">Những chỗ đọc được nhưng đáng ngờ (thiếu đáp án đúng, chỉ có một đáp án...),
/// kèm số dòng. Câu hỏi hỏng bị bỏ qua chứ không đánh hỏng cả lần nhập.</param>
public record WordQuestionDocument(
    IReadOnlyList<ParsedQuestionGroup> GroupList, IReadOnlyList<(int Line, string Message)> WarningList);
