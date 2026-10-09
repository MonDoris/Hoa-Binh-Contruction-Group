// File: Application/DTOs/LearningPathDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Cấu trúc khóa học (= SCORM Organization). <see cref="ItemList"/> theo đúng thứ tự cây (tiền thứ tự: chương
/// rồi tới các mục trong chương), mỗi mục mang <see cref="PathItemDto.ParentItemId"/> và độ sâu để dựng lại cây.
/// IsVirtual = khóa học chưa từng dựng cấu trúc, đây là cấu trúc mặc định hệ thống tự xếp (chưa lưu).</summary>
public record LearningPathDto(int Id, int CourseId, string CourseTitle, string Name,
    string? Description, bool IsSequential, IReadOnlyList<PathItemDto> ItemList,
    IReadOnlyList<UnplacedResourceDto>? UnplacedList = null, bool IsVirtual = false);

// ParentLessonId: với bước Quiz / Simulation gắn theo một bài học ("con" của bài học đó) — id bài học cha, dùng để
// gom bước đó vào dưới bài học khi hiển thị. Cùng nghĩa với PathProgressDto.ParentLessonId bên học viên.
// ParentItemId: chương chứa mục (null = ngay dưới gốc). CustomTitle: tên người soạn đặt riêng cho mục (tên chương, hoặc
// tên hiển thị khác tên tài nguyên). ActivityId: định danh hoạt động trong cây SCORM (L5, Q3, M12...) — để mở xem thử
// đúng mục. Note: lý do mục không được giao cho học viên (tài nguyên đã ẩn, mô phỏng chưa có tình huống...).
public record PathItemDto(int Id, string ItemType, int? LessonId, int? QuizId,
    string Title, int DurationMinutes, int OrderIndex, bool IsRequired,
    DateTime? StartDate, DateTime? EndDate, int? ParentLessonId, int? ScenarioSceneId, int? ScormPackageId = null,
    int? ParentItemId = null, string? CustomTitle = null, bool IsVisible = true, int? AttemptLimit = null,
    decimal? CompletionThreshold = null, string? ActivityId = null, int Depth = 1, string? Note = null);

/// <summary>Tài nguyên của khóa học CHƯA nằm trong cấu trúc — học viên không thấy cho tới khi được đưa vào.</summary>
public record UnplacedResourceDto(string ItemType, int Id, string Title, int? ParentLessonId, string? Note);

public class LearningPathRequest
{
    [Required] public int CourseId { get; set; }
    [Required, MaxLength(250)] public string Name { get; set; } = null!;
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsSequential { get; set; } = true;
}

public class PathItemRequest
{
    [Required] public PathItemType ItemType { get; set; }
    public int? LessonId { get; set; }
    public int? QuizId { get; set; }
    public int? ScenarioSceneId { get; set; }
    public int? ScormPackageId { get; set; }

    /// <summary>Chương chứa mục mới; null = ngay dưới gốc.</summary>
    public int? ParentItemId { get; set; }

    /// <summary>Bắt buộc với chương (Module); với mục lá là tên hiển thị riêng (bỏ trống = tên tài nguyên).</summary>
    [MaxLength(250)] public string? Title { get; set; }

    /// <summary>Không dùng nữa — mục mới luôn nối vào cuối chương / cuối khóa học; đổi chỗ bằng kéo thả.</summary>
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>Sửa lịch mở/khóa theo thời gian của một bước trong lộ trình đã có sẵn.</summary>
public class PathItemScheduleRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsRequired { get; set; } = true;
}

/// <summary>Thiết lập một mục của cấu trúc — chính là phần sequencing của &lt;item&gt; tương ứng trong SCORM.</summary>
public class PathItemUpdateRequest
{
    [MaxLength(250)] public string? Title { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>limitConditions attemptLimit; null / 0 = không giới hạn.</summary>
    [Range(0, 1000)] public int? AttemptLimit { get; set; }

    /// <summary>adlcp:completionThreshold (0..1) — chỉ áp cho gói SCORM nhập ngoài.</summary>
    [Range(0, 1)] public decimal? CompletionThreshold { get; set; }
}

/// <summary>Vị trí một mục sau khi kéo thả. Danh sách gửi lên theo đúng thứ tự cây mới (tiền thứ tự).</summary>
public class PathItemPlacement
{
    [Required] public int Id { get; set; }
    public int? ParentItemId { get; set; }
}
