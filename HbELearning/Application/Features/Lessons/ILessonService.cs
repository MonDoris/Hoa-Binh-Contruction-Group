// File: Application/Features/Lessons/ILessonService.cs

namespace HbELearning.Application.Features.Lessons;

/// <summary>Các ca sử dụng của chức năng Lesson. Tầng API gọi qua cổng này (xem API/Controllers/LessonService.cs).</summary>
public interface ILessonService
{
    Task<Result> GetByCourse(int courseId);

    Task<Result> GetById(int id);

    Task<Result> Create(LessonRequest request);

    /// <summary>Tạo bài học kèm (tùy chọn) một bài kiểm tra gắn ngay theo sau — bài kiểm tra là "con" của bài học,
    /// người học phải làm xong bài kiểm tra này mới được mở bài học tiếp theo. Nếu khóa học đã có lộ trình học
    /// tập, cặp bài học + bài kiểm tra được nối thêm vào cuối lộ trình (không đụng tới lịch các bước đã có).</summary>
    Task<Result> CreateWithQuiz(LessonWithQuizRequest request);

    Task<Result> Update(int id, LessonRequest request);

    /// <summary>Sắp xếp lại thứ tự nhiều bài học cùng lúc (kéo thả ở giao diện).</summary>
    Task<Result> Reorder(List<ReorderItem> itemList);

    Task<Result> Delete(int id);
}
