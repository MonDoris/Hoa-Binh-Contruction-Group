// File: Application/DTOs/ScormDto.cs
using System.ComponentModel.DataAnnotations;
using HbELearning.Application.Features.Scorm.Runtime;
using HbELearning.Application.Features.Scorm.Sequencing;

namespace HbELearning.Application.DTOs;

// ==================== GÓI (phía người soạn) ====================

public record ScormPackageDto(
    int Id, int CourseId, string Title, string Version, string? SchemaVersion,
    int LaunchableCount, int ScoCount, long FileSize, long ExtractedSize, int FileCount, int DurationMinutes,
    string? OriginalFileName, DateTime CreatedAt, string CreatedByName,
    int LearnerCount, int? PathItemId, IReadOnlyList<ScormTreeNodeDto> Tree);

public record ScormTreeNodeDto(string Id, string Title, int Depth, bool IsLeaf, bool IsVisible, bool IsSco,
    string? LaunchUrl, bool HasSequencingRule);

/// <summary>Sửa thông tin hiển thị của gói — nội dung gói thì không sửa được, muốn đổi phải nhập gói mới.</summary>
public class ScormPackageUpdateRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [Range(0, 100000)] public int DurationMinutes { get; set; }
}

/// <summary>Thêm gói SCORM vào một khóa học có sẵn — thành một mục (cụm) của cấu trúc khóa học.</summary>
public class ScormPackageImportRequest
{
    [MaxLength(250)] public string? Title { get; set; }
    public bool IsRequired { get; set; } = true;

    /// <summary>Chương sẽ chứa gói; null = cuối khóa học.</summary>
    public int? ParentItemId { get; set; }
}

/// <summary>Nhập gói SCORM thành NGUYÊN một khóa học mới — các trường giống tạo khóa học, riêng tên khóa học được
/// bỏ trống (lấy theo tên trong manifest).</summary>
public class ScormCourseImportRequest
{
    [MaxLength(250)] public string? Title { get; set; }
    [MaxLength(4000)] public string? Description { get; set; }
    [Range(0, 100000)] public int DurationMinutes { get; set; }
    [Range(0, 100)] public decimal PassScore { get; set; } = 70m;
    public bool IsMandatory { get; set; }
    [Range(1, 3650)] public int? DeadlineDay { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập ngày bắt đầu khóa học.")] public DateTime? StartDate { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập ngày kết thúc khóa học.")] public DateTime? EndDate { get; set; }
    public int? ApprovalWorkflowId { get; set; }
    public int? DepartmentId { get; set; }
    public bool AllDepartment { get; set; }
}

// ==================== PHIÊN HỌC (phía trình phát) ====================

/// <summary>Mở trình phát. Đúng MỘT trong ba: lượt học của học viên (mở khóa học), khóa học cần xem thử, hoặc một gói
/// nhập ngoài cần xem thử riêng.</summary>
public class ScormLaunchRequest
{
    public int? EnrollmentId { get; set; }
    public int? CourseId { get; set; }
    public int? PackageId { get; set; }

    /// <summary>Mở thẳng hoạt động này (L5, Q3, P2...) nếu sequencing cho chọn; bỏ trống = học tiếp đúng chỗ.</summary>
    [MaxLength(250)] public string? TargetActivityId { get; set; }

    /// <summary>Bỏ lượt đang dở, học lại từ đầu (lượt thử mới trên toàn gói).</summary>
    public bool Restart { get; set; }
}

/// <summary>Dữ liệu run-time trình phát gửi kèm khi Commit / Terminate.</summary>
public class ScormRuntimePayload
{
    /// <summary>Toàn bộ mô hình dữ liệu CMI hiện tại của SCO (JSON) — lưu nguyên văn để lần sau học tiếp.</summary>
    [MaxLength(2_000_000)] public string? CmiJson { get; set; }
    [Required] public ScormRuntimeReport Report { get; set; } = new();
    public int InteractionCount { get; set; }
}

public class ScormCommitRequest
{
    [Required, MaxLength(32)] public string SessionId { get; set; } = null!;
    [Required, MaxLength(250)] public string ActivityId { get; set; } = null!;
    [Required] public ScormRuntimePayload Runtime { get; set; } = new();
}

public class ScormNavigateRequest
{
    [Required, MaxLength(32)] public string SessionId { get; set; } = null!;

    /// <summary>SCO vừa kết thúc (Terminate) — kèm dữ liệu cuối cùng của nó. Null khi điều hướng lúc không có SCO
    /// nào đang mở (VD bấm mục lục sau khi nội dung đã tự kết thúc, hoặc nội dung là asset).</summary>
    [MaxLength(250)] public string? ActivityId { get; set; }
    public ScormRuntimePayload? Runtime { get; set; }

    /// <summary>start, resumeAll, continue, previous, choice, jump, exit, exitAll, abandon, abandonAll,
    /// suspendAll — hoặc rỗng / "_none_" khi SCO kết thúc mà không xin điều hướng gì.</summary>
    [MaxLength(20)] public string? Request { get; set; }
    [MaxLength(250)] public string? TargetActivityId { get; set; }
}

public record ScormSharedDataDto(string Id, string Store, bool Readable, bool Writable);

/// <summary>Giá trị LMS khởi tạo cho mô hình dữ liệu khi mở một SCO.</summary>
public record ScormCmiInitDto(
    string LearnerId, string LearnerName, string Credit, string Mode, string Entry,
    string? LaunchData, double? CompletionThreshold, double? ScaledPassingScore,
    string? MaxTimeAllowed, string? TimeLimitAction, decimal? MasteryScore, int TotalTimeSecond,
    IReadOnlyList<ScormObjectiveReport> ObjectiveList, IReadOnlyList<ScormSharedDataDto> SharedDataList);

public record ScormDeliveryDto(
    string ActivityId, string Title, string LaunchUrl, bool IsSco, IReadOnlyList<string> HideLmsUi,
    // Dữ liệu CMI đã lưu của lượt thử đang học tiếp; null = lượt thử mới.
    string? CmiJson,
    ScormCmiInitDto Init,
    // Bộ API nội dung này tìm: "2004" (API_1484_11) hay "1.2" (API) — gói 1.2 nhúng trong khóa học 2004.
    string RuntimeVersion = "2004");

public record ScormSummaryDto(string CompletionStatus, string SuccessStatus, decimal? ScorePercent,
    int TotalTimeSecond, int AttemptCount, bool IsSuspended, DateTime? CompletedAt);

public record ScormNavStateDto(bool CanContinue, bool CanPrevious, bool HasCurrent,
    IReadOnlyList<ScormActivityStatus> ActivityList,
    IReadOnlyList<string> ChoiceValidIdList, IReadOnlyList<string> JumpValidIdList);

public record ScormSessionDto(
    int RegistrationId, string SessionId, int PackageId, string PackageTitle, string Version,
    int? EnrollmentId, bool IsPreview, int CourseId, string CourseTitle,
    ScormDeliveryDto? Delivery, bool EndSession, string? Message,
    ScormNavStateDto Nav, ScormSummaryDto Summary,
    // Gói của chính khóa học (cây dựng từ cấu trúc khóa học) — khác với xem thử riêng một gói nhập ngoài.
    bool IsCourse = false);

public record ScormCommitResultDto(ScormNavStateDto Nav, ScormSummaryDto Summary);

// ==================== BÁO CÁO ====================

public record ScormReportRowDto(
    int RegistrationId, int EnrollmentId, int UserId, string EmployeeCode, string FullName, string? DepartmentName,
    int PackageId, string PackageTitle,
    string CompletionStatus, string SuccessStatus, decimal? ScorePercent, decimal? BestScorePercent,
    int TotalTimeSecond, int AttemptCount, bool IsSuspended, DateTime? LastAccessedAt, DateTime? CompletedAt);

public record ScormInteractionDto(string Id, string? Type, string? Description, string? LearnerResponse,
    string? CorrectResponse, string? Result, string? Weighting, string? Latency, string? Timestamp);

public record ScormActivityReportDto(string ActivityId, string Title, bool IsSco,
    string CompletionStatus, string SuccessStatus, decimal? ScoreScaled, decimal? ScoreRaw, decimal? ScoreMin, decimal? ScoreMax,
    decimal? ProgressMeasure, int TotalTimeSecond, int SessionCount, int AttemptCount, DateTime? UpdatedAt,
    string? Location, IReadOnlyList<ScormInteractionDto> InteractionList);

public record ScormRegistrationDetailDto(ScormReportRowDto Row, IReadOnlyList<ScormActivityReportDto> ActivityList);

// ==================== XUẤT GÓI ====================

public record ScormExportItemDto(string Kind, string Title, bool Included, string? Note);

public record ScormExportPreviewDto(int CourseId, string CourseTitle, bool CanDownloadOriginal,
    IReadOnlyList<ScormExportItemDto> ItemList);
