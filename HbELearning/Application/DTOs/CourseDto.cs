// File: Application/DTOs/CourseDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record CourseListDto(
    int Id, string Code, string Title, string? Description, string? ThumbnailUrl,
    string Status, int DurationMinutes, decimal PassScore, bool IsMandatory,
    int LessonCount, int QuizCount, int EnrollmentCount,
    int CreatedById, string CreatedByName, int? CreatedByDepartmentId, DateTime CreatedAt,
    DateTime StartDate, DateTime EndDate,
    string? RejectionReason, string? RejectedByRoleName, string? HrApproverLabel,
    int? ApprovalWorkflowId = null, string? ApprovalWorkflowName = null, int CurrentStepOrder = 0,
    IReadOnlyList<string>? ApprovalStepNameList = null, bool CanApprove = false,
    string? CurrentStepApproverName = null, bool CanReassignApprover = false,
    // Chỉ có ở mục gợi ý khóa học (GetMyAvailable): vì sao khóa này hợp với học viên —
    // "DepartmentPosition" | "Department" | "Position" | "All".
    string? SuggestReason = null,
    // Số gói SCORM của khóa học — khóa học nhập nguyên từ gói SCORM có 0 bài học, 0 bài kiểm tra.
    int ScormPackageCount = 0);

public record CourseDetailDto(
    int Id, string Code, string Title, string? Description, string? ThumbnailUrl,
    string Status, int DurationMinutes, decimal PassScore, bool IsMandatory, int? DeadlineDay,
    DateTime StartDate, DateTime EndDate,
    int CreatedById, string CreatedByName, int? CreatedByDepartmentId, DateTime CreatedAt, DateTime? UpdatedAt,
    string? RejectionReason, string? RejectedByRoleName, string? HrApproverLabel,
    IReadOnlyList<CourseTargetDto> TargetList,
    IReadOnlyList<LessonListDto> LessonList,
    IReadOnlyList<QuizListDto> QuizList,
    int? ApprovalWorkflowId = null, string? ApprovalWorkflowName = null, int CurrentStepOrder = 0,
    IReadOnlyList<string>? ApprovalStepNameList = null, bool CanApprove = false,
    string? CurrentStepApproverName = null, bool CanReassignApprover = false);

public record CourseTargetDto(int Id, int? DepartmentId, string? DepartmentName, int? PositionId, string? PositionName);

/// <summary>Một bước trong lộ trình duyệt nội dung — dùng để hiển thị lịch sử duyệt (ai duyệt/từ chối, khi nào, vì sao).</summary>
public record CourseApprovalHistoryDto(
    int Id, int ActorUserId, string ActorUserName, string ActorRoleName,
    string FromStatus, string ToStatus, string? Reason, DateTime CreatedAt,
    string? StepName = null);

/// <summary>Kết quả nhập NGUYÊN một tài liệu Word/PowerPoint/PDF thành bài học.</summary>
/// <param name="Mode">"word-html" = đã dựng lại nội dung thành HTML; "word-split" = một file Word tách thành
/// nhiều bài; "pptx-slides" = dựng từng slide; "pdf-embed" = nhúng nguyên file PDF.</param>
/// <param name="LessonId">Bài học đầu tiên được tạo.</param>
/// <param name="LessonCount">Số bài học đã tạo (chỉ khác 1 khi tách một file Word thành nhiều bài).</param>
public record DocumentImportResultDto(
    int LessonId, string Title, string Mode, int ImageCount, string? ResourceUrl, string Message,
    int LessonCount = 1, IReadOnlyList<string>? TitleList = null);

/// <summary>Danh sách bài học dự kiến tách ra từ một file Word — xem trước, chưa tạo gì cả.</summary>
/// <param name="SplitRule">Dấu hiệu đã dùng để tách: "heading" | "pattern" | "none".</param>
public record DocumentOutlineDto(string SplitRule, IReadOnlyList<DocumentSectionDto> SectionList);

/// <param name="Index">Số thứ tự của phần trong tài liệu — gửi lại đúng số này khi chọn phần muốn tạo.</param>
public record DocumentSectionDto(int Index, string Title, int CharacterCount, int ImageCount);

/// <summary>Một người có thể được chỉ định duyệt một bước, kèm đủ thông tin để người soạn phân biệt hai
/// người trùng tên trong ô chọn.</summary>
public record ApproverCandidateDto(
    int Id, string FullName, string EmployeeCode, string RoleName, string? DepartmentName, string? PositionName);

/// <summary>Một bước của luồng cấp duyệt kèm danh sách người có thể duyệt — đổ vào hộp thoại "Gửi duyệt" để
/// người soạn chỉ định người duyệt cho những bước cần chỉ định.</summary>
/// <param name="ApproverMode">"AnyOfRole" = gửi cả nhóm, giao diện chỉ liệt kê cho biết; "PickOne" = bắt buộc
/// chọn một người; "FixedUser" = Admin đã ghim sẵn, giao diện chỉ hiển thị tên.</param>
/// <param name="SelectedUserId">Người đã được chỉ định ở lần gửi duyệt trước (nếu có) — dùng để chọn sẵn lại
/// khi khóa học bị từ chối rồi gửi lại, khỏi phải chọn từ đầu.</param>
public record CourseStepApproverOptionDto(
    int StepOrder, string StepName, string ApproverMode, int? SelectedUserId,
    IReadOnlyList<ApproverCandidateDto> CandidateList);

/// <summary>Yêu cầu đổi người duyệt của một bước khi khóa học đang chờ duyệt — dùng khi người được chỉ định
/// nghỉ phép / nghỉ việc, không có nó thì khóa học kẹt lại ở bước đó vĩnh viễn.</summary>
public class CourseReassignApproverRequest
{
    [Range(1, int.MaxValue)]
    public int StepOrder { get; set; }

    /// <summary>Người duyệt mới. Khác với lúc gửi duyệt, ở đây bắt buộc phải có người: hành động này là
    /// "giao bước cho ai" — CourseService tự kiểm để báo bằng tiếng Việt thay vì lỗi ràng buộc thô.</summary>
    public int UserId { get; set; }
}

/// <summary>Yêu cầu từ chối nội dung — lý do từ chối là bắt buộc.</summary>
public class CourseRejectRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do từ chối."), MinLength(3), MaxLength(1000)]
    public string Reason { get; set; } = null!;
}

public class CourseRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(500)] public string? ThumbnailUrl { get; set; }
    [Range(0, 100000)] public int DurationMinutes { get; set; }
    [Range(0, 100)] public decimal PassScore { get; set; } = 70m;
    public bool IsMandatory { get; set; }
    [Range(1, 3650)] public int? DeadlineDay { get; set; }

    /// <summary>Ngày bắt đầu / kết thúc khóa học — bắt buộc. Để kiểu nullable chỉ để [Required] bắt được ô bị bỏ
    /// trống: với DateTime thường, thiếu trường vẫn thành 01/01/0001 và lọt qua kiểm tra.</summary>
    [Required(ErrorMessage = "Vui lòng nhập ngày bắt đầu khóa học.")] public DateTime? StartDate { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập ngày kết thúc khóa học.")] public DateTime? EndDate { get; set; }

    /// <summary>Luong cap duyet nguoi soan chon cho khoa hoc (do Admin dinh nghia san). Null = khong chon,
    /// khoa hoc chay chuoi duyet co dinh cu. Chi doi duoc khi khoa hoc con o trang thai Nhap / Tu choi.</summary>
    /// <summary>Người soạn CHỈ chọn luồng — người duyệt của từng bước do người dựng luồng ghim sẵn trong
    /// định nghĩa luồng, nên biểu mẫu khóa học không còn trường chỉ định người duyệt nào.</summary>
    public int? ApprovalWorkflowId { get; set; }
}

/// <summary>Yêu cầu tạo khóa học — mã khóa học do hệ thống tự sinh từ mã phòng ban.</summary>
public class CourseCreateRequest : CourseRequest
{
    /// <summary>Phòng ban lấy mã làm tiền tố cho mã khóa học. Bỏ trống khi <see cref="AllDepartment"/> = true.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Khóa học dùng chung toàn công ty — mã sinh ra theo tiền tố ALL (ALL-001, ALL-002...).
    /// Tách hẳn thành một cờ riêng thay vì quy ước "DepartmentId để trống = toàn công ty": bên gọi quên
    /// gửi phòng ban sẽ bị báo lỗi, chứ không lặng lẽ tạo ra một khóa học phát cho cả công ty.</summary>
    public bool AllDepartment { get; set; }
}

public class CourseTargetRequest
{
    public int? DepartmentId { get; set; }
    public int? PositionId { get; set; }
}
