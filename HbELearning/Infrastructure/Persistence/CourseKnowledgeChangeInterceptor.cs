// File: Infrastructure/Persistence/CourseKnowledgeChangeInterceptor.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HbELearning.Infrastructure.Persistence;

/// <summary>Bắt mọi lần lưu có đụng tới khóa học / bài học và báo cho Pico B học lại khóa đó.
///
/// Đặt ở tầng lưu dữ liệu chứ không gọi tay ở từng nút: một khóa học trở thành "Đã xuất bản" qua nhiều đường
/// (Giám Đốc Nhân sự duyệt bước cuối, luồng cấp duyệt tùy biến, ...) và bài học thì được thêm/sửa/ẩn/xóa ở
/// nhiều chỗ. Gọi tay thì sớm muộn có đường bị quên — đúng lỗi "xuất bản rồi mà Pico B không biết gì".
///
/// Báo thừa là vô hại: khóa chưa xuất bản thì lượt học bỏ qua, bài không đổi chữ nào thì trùng vân tay và
/// không gọi Ollama (xem <see cref="CourseKnowledgeService"/>).</summary>
public sealed class CourseKnowledgeChangeInterceptor : SaveChangesInterceptor
{
    private readonly ICourseKnowledgeSyncQueue _queue;

    public CourseKnowledgeChangeInterceptor(ICourseKnowledgeSyncQueue queue) => _queue = queue;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CollectChangedCourse(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CollectChangedCourse(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>Báo ngay lúc SẮP lưu (khi trạng thái thay đổi còn đọc được). Lưu hỏng thì chỉ sinh ra một lượt
    /// học thừa — worker còn chờ vài giây gom việc nên lượt đó luôn chạy sau khi giao dịch đã xong.</summary>
    private void CollectChangedCourse(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            switch (entry.Entity)
            {
                case Course course when entry.State != EntityState.Added && IsKnowledgeRelevant(entry):
                    _queue.Enqueue(course.Id);
                    break;

                case Lesson lesson:
                    _queue.Enqueue(lesson.CourseId);
                    // Bài học bị chuyển sang khóa khác: khóa cũ cũng phải dọn phần đã học.
                    if (entry.State == EntityState.Modified
                        && entry.Property(nameof(Lesson.CourseId)).OriginalValue is int oldCourseId
                        && oldCourseId != lesson.CourseId)
                        _queue.Enqueue(oldCourseId);
                    break;
            }
        }
    }

    /// <summary>Khóa học mới tạo luôn là bản nháp nên bỏ qua. Khóa đã có thì chỉ quan tâm những cột làm đổi
    /// nội dung Pico B học: trạng thái (xuất bản / lưu trữ / trả về nháp) và phần giới thiệu.</summary>
    private static bool IsKnowledgeRelevant(EntityEntry entry)
        => entry.State == EntityState.Deleted
           || IsModified(entry, nameof(Course.Status))
           || IsModified(entry, nameof(Course.Title))
           || IsModified(entry, nameof(Course.Code))
           || IsModified(entry, nameof(Course.Description))
           || IsModified(entry, nameof(Course.DurationMinutes))
           || IsModified(entry, nameof(Course.IsMandatory));

    private static bool IsModified(EntityEntry entry, string propertyName)
        => entry.Property(propertyName).IsModified;
}
