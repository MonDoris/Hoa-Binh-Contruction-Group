// File: Application/Features/LearningPaths/ILearningPathService.cs

namespace HbELearning.Application.Features.LearningPaths;

/// <summary>Cấu trúc khóa học (SCORM Organization). Tầng API gọi qua cổng này (xem API/Controllers/LearningPathController.cs).</summary>
public interface ILearningPathService
{
    /// <summary>Xem cấu trúc khóa học — mở cho mọi cấp duyệt nội dung (Trưởng phòng/Giám Đốc) chứ không chỉ người tạo,
    /// để họ xem được khi duyệt khóa học đã gửi lên. Khóa học chưa từng dựng cấu trúc thì trả cấu trúc mặc định
    /// (IsVirtual) — đúng thứ tự học viên đang thấy.</summary>
    Task<Result> GetByCourse(int courseId);

    /// <summary>Lưu cấu trúc mặc định xuống nếu khóa học chưa có, để người soạn bắt đầu chỉnh sửa.</summary>
    Task<Result> EnsureOrganization(int courseId);

    Task<Result> Create(LearningPathRequest request);

    Task<Result> Update(int id, LearningPathRequest request);

    /// <summary>Dựng lại cấu trúc từ toàn bộ bài học + bài kiểm tra + mô phỏng + gói SCORM hiện có của khóa học.</summary>
    Task<Result> AutoGenerate(int courseId);

    /// <summary>Thêm chương, hoặc thêm một tài nguyên chưa có vào cấu trúc (cuối chương được chọn).</summary>
    Task<Result> AddItem(int pathId, PathItemRequest request);

    /// <summary>Thiết lập một mục: tên, bắt buộc, hiển thị, lịch, giới hạn lượt, ngưỡng hoàn thành.</summary>
    Task<Result> UpdateItem(int itemId, PathItemUpdateRequest request);

    /// <summary>Gán/sửa khoảng ngày mở-khóa theo lịch cho một bước đã có trong lộ trình.</summary>
    Task<Result> UpdateItemSchedule(int itemId, PathItemScheduleRequest request);

    /// <summary>Lưu cả cây sau khi kéo thả (chương cha + thứ tự).</summary>
    Task<Result> MoveTree(int pathId, List<PathItemPlacement> placementList);

    Task<Result> ReorderItem(List<ReorderItem> itemList);

    Task<Result> DeleteItem(int itemId);

    /// <summary>Đưa tài nguyên vừa tạo vào cấu trúc khóa học (gọi từ nơi tạo bài học / bài kiểm tra / mô phỏng / gói).</summary>
    Task AppendResourceAsync(int courseId, PathItemType type, int resourceId, int? parentItemId = null, bool isRequired = true);

    /// <summary>Bài kiểm tra / mô phỏng vừa được sửa (gọi SAU khi đã lưu LessonId mới): đưa mục của nó trong cấu trúc về
    /// ngay sau bài học gắn kèm. <paramref name="lessonChanged"/> = bài học gắn kèm vừa đổi so với trước khi sửa.</summary>
    Task AlignAttachedResourceAsync(int courseId, PathItemType type, int resourceId, bool lessonChanged);
}
