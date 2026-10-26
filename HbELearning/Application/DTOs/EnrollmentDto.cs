// File: Application/DTOs/EnrollmentDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public class AssignRequest
{
    [Required] public int CourseId { get; set; }
    [Required, MinLength(1, ErrorMessage = "Phải chọn ít nhất một người học")]
    public List<int> UserIdList { get; set; } = new();
    public DateTime? DueDate { get; set; }
}

public class AssignByTargetRequest
{
    [Required] public int CourseId { get; set; }
    public int? DepartmentId { get; set; }
    public int? PositionId { get; set; }
    public DateTime? DueDate { get; set; }
}

public record EnrollmentListDto(
    int Id, int CourseId, string CourseCode, string CourseTitle, string? ThumbnailUrl,
    int UserId, string UserFullName, string EmployeeCode, string? DepartmentName,
    string Status, decimal ProgressPercent, decimal? FinalScore,
    DateTime AssignedAt, DateTime? DueDate, DateTime? StartedAt, DateTime? CompletedAt,
    bool IsOverdue, bool HasCertificate,
    // Điểm sao trung bình của KHÓA HỌC (không phải đánh giá riêng của người này) — hiện ở góc thẻ khóa học.
    decimal? CourseAverageRating = null, int CourseReviewCount = 0);

/// <summary>Một bài học được ghim lên lịch học của người dùng hiện tại.
///
/// ScheduledAt lấy từ Lesson.CreatedAt — ngày bài học được tạo chính là ngày nó xuất hiện trên lịch.
/// Status là tiến độ của CHÍNH người học này trên bài đó ("NotStarted" khi chưa có bản ghi LessonProgress),
/// để lịch tô màu bài đã xong khác bài còn dang dở.</summary>
public record ScheduleItemDto(
    int EnrollmentId, int CourseId, string CourseTitle,
    int LessonId, string LessonTitle, string ContentType,
    int DurationMinutes, DateTime ScheduledAt, string Status);

public record EnrollmentDetailDto(
    int Id, int CourseId, string CourseCode, string CourseTitle, string? Description,
    decimal CoursePassScore, bool IsSequential,
    int UserId, string UserFullName,
    string Status, decimal ProgressPercent, decimal? FinalScore,
    DateTime AssignedAt, DateTime? DueDate, DateTime? StartedAt, DateTime? CompletedAt,
    bool HasCertificate, int? CertificateId,
    IReadOnlyList<PathProgressDto> PathList);

/// <summary>Một bước trong lộ trình kèm trạng thái học của người dùng hiện tại.
/// AttemptUsed: số lượt làm bài ĐÃ DÙNG TRONG NGÀY hôm nay (giới hạn MaxAttempt được làm mới mỗi ngày).
/// HasEverAttempted: đã từng làm bài này (bất kỳ ngày nào) hay chưa — dùng để hiện nút "Lịch sử làm bài".</summary>
public record PathProgressDto(
    int PathItemId, string ItemType, int? LessonId, int? QuizId, string Title,
    int DurationMinutes, int OrderIndex, bool IsRequired,
    string Status, bool IsUnlocked,
    decimal? BestScore, int AttemptUsed, int MaxAttempt, int LastPositionSecond,
    DateTime? StartDate, DateTime? EndDate, bool HasEverAttempted, int? ScenarioSceneId,

    /// <summary>Dạng nội dung của bài học ("Text" | "Document" | "Video" | "Image" | "Link"); null với bước
    /// Kiểm tra / Mô phỏng 3D. Cây nội dung bên phải màn hình học dùng nó để gắn đúng nhãn và biểu tượng
    /// cho nhánh con, thay vì gọi /lesson/{id} cho từng bước chỉ để biết bài đó là video hay lý thuyết.</summary>
    string? ContentType = null,

    /// <summary>Số câu hỏi chèn giữa video của bài học này. Cây nội dung hiện sẵn con số ("3 câu hỏi") để
    /// học viên biết bài nào có gì trước khi mở, còn nội dung từng câu thì bấm mở mới tải.</summary>
    int VideoQuestionCount = 0,

    /// <summary>Bước Kiểm tra / Mô phỏng 3D này được gắn vào bài học nào (Quiz.LessonId, ScenarioScene.LessonId).
    /// null = bước đứng riêng, VD bài thi cuối khóa. Cây nội dung gom bước con vào đúng bài học cha nhờ nó.</summary>
    int? ParentLessonId = null,

    /// <summary>Số chương (đoạn nội dung theo thời gian) của video bài học này. Cây nội dung dựa vào nó để
    /// biết có gì để mở ra hay không: video chỉ có chương mà chưa có câu hỏi nào vẫn phải bung được mục lục,
    /// nếu chỉ nhìn VideoQuestionCount thì nhánh đó câm luôn, học viên tưởng video không chia phần.</summary>
    int VideoChapterCount = 0,

    /// <summary>Gói SCORM của bước loại Scorm; null với các loại bước khác.</summary>
    int? ScormPackageId = null);
