// File: Application/Features/Progress/IProgressService.cs

namespace HbELearning.Application.Features.Progress;

/// <summary>Các ca sử dụng của chức năng Progress. Tầng API gọi qua cổng này (xem API/Controllers/ProgressService.cs).</summary>
public interface IProgressService
{
    /// <summary>Trạng thái toàn bộ lộ trình của một bản ghi phân công.</summary>
    Task<Result> GetPath(int enrollmentId);

    /// <summary>Bắt đầu học một bài — tạo bản ghi tiến độ nếu chưa có.</summary>
    Task<Result> StartLesson(int enrollmentId, int lessonId);

    /// <summary>Cập nhật thời gian đã học / vị trí video đang xem dở (gọi định kỳ từ frontend).</summary>
    Task<Result> Heartbeat(int enrollmentId, int lessonId, int timeSpentSecond = 0, int lastPositionSecond = 0, int maxWatchedSecond = 0);

    /// <summary>Đánh dấu hoàn thành một bài học — kích hoạt tính lại tiến độ khóa học.</summary>
    Task<Result> CompleteLesson(int enrollmentId, int lessonId);
}
