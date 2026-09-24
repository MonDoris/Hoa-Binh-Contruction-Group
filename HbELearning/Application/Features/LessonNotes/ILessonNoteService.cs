// File: Application/Features/LessonNotes/ILessonNoteService.cs

namespace HbELearning.Application.Features.LessonNotes;

/// <summary>Các ca sử dụng của chức năng LessonNote. Tầng API gọi qua cổng này (xem API/Controllers/LessonNoteService.cs).</summary>
public interface ILessonNoteService
{
    /// <summary>Ghi chú của chính người đang đăng nhập trong một bài học, sắp theo mốc thời gian tăng dần —
    /// đọc từ trên xuống là đi dọc video từ đầu tới cuối, đúng thứ tự họ đã học.</summary>
    Task<Result> GetList(int enrollmentId, int lessonId);

    Task<Result> Create(LessonNoteCreateRequest request);

    Task<Result> Update(int id, LessonNoteUpdateRequest request);

    Task<Result> Delete(int id);
}
