// File: Application/Features/LearningPaths/ILearningPathService.cs

namespace HbELearning.Application.Features.LearningPaths;

/// <summary>Các ca sử dụng của chức năng LearningPath. Tầng API gọi qua cổng này (xem API/Controllers/LearningPathService.cs).</summary>
public interface ILearningPathService
{
    /// <summary>Xem lộ trình học tập — mở cho mọi cấp duyệt nội dung (Trưởng phòng/Giám Đốc) chứ không chỉ người tạo,
    /// để họ xem được lộ trình khi duyệt khóa học đã gửi lên. Trước đây bị giới hạn Admin/Trưởng Nhóm mất do
    /// [Authorize(Roles=...)] ở cấp class + method cộng dồn với nhau (AND) thay vì cái ở method ghi đè cái ở class.</summary>
    Task<Result> GetByCourse(int courseId);

    Task<Result> Create(LearningPathRequest request);

    Task<Result> Update(int id, LearningPathRequest request);

    /// <summary>Tự sinh lộ trình từ toàn bộ bài học + bài kiểm tra hiện có của khóa học.</summary>
    Task<Result> AutoGenerate(int courseId);

    Task<Result> AddItem(int pathId, PathItemRequest request);

    /// <summary>Gán/sửa khoảng ngày mở-khóa theo lịch cho một bước đã có trong lộ trình.</summary>
    Task<Result> UpdateItemSchedule(int itemId, PathItemScheduleRequest request);

    Task<Result> ReorderItem(List<ReorderItem> itemList);

    Task<Result> DeleteItem(int itemId);
}
