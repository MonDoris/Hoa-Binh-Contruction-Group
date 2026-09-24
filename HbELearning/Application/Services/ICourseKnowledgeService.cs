// File: Application/Services/ICourseKnowledgeService.cs

namespace HbELearning.Application.Services;

public interface ICourseKnowledgeService
{
    /// <summary>Đưa nội dung các khóa học người dùng đang được học vào kho tri thức AI, để Pico B trả lời
    /// được câu hỏi về chính bài học chứ không chỉ về tài liệu Admin tải lên.
    ///
    /// Chạy lại nhiều lần vô hại: bài học không đổi chữ nào thì bỏ qua (so vân tay), bài học đã sửa thì nạp
    /// đè, bài học/khóa học đã xóa hoặc thôi không còn ai học thì dọn đi.
    ///
    /// <paramref name="isForced"/> = true thì nạp lại tất cả kể cả phần vân tay không đổi — dùng khi đổi
    /// model nhúng hoặc khi nghi kho tri thức đã hỏng.</summary>
    Task<CourseKnowledgeSyncResultDto> SyncAsync(
        bool isForced = false, int? courseId = null, CancellationToken cancellationToken = default);

    /// <summary>Khóa học nào đã được AI học, học tới đâu, lần gần nhất là khi nào — cho màn hình quản trị.</summary>
    Task<CourseKnowledgeStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
