// File: Domain/Entities/Course.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Course
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Cho phép nội dung HTML định dạng (in đậm, in nghiêng, danh sách...) từ trình soạn thảo Quill.</summary>
    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    public CourseStatus Status { get; set; } = CourseStatus.Draft;

    /// <summary>LUỒNG CẤP DUYỆT mà người soạn đã chọn cho khóa học này (do Admin định nghĩa sẵn — xem
    /// <see cref="ApprovalWorkflow"/>). Null = không chọn luồng nào, khóa học chạy chuỗi duyệt cố định cũ
    /// (Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự) để dữ liệu cũ không đổi hành vi.</summary>
    public int? ApprovalWorkflowId { get; set; }
    public ApprovalWorkflow? ApprovalWorkflow { get; set; }

    /// <summary>Bước đang chờ duyệt trong luồng, khớp ApprovalWorkflowStep.OrderIndex (đánh số từ 1).
    /// 0 = chưa gửi duyệt / đã xong. Khi khóa học bị Từ chối, giá trị này được GIỮ NGUYÊN để giao diện biết
    /// bước nào đã từ chối; gửi duyệt lại sẽ đưa về 1. Chỉ có ý nghĩa khi ApprovalWorkflowId khác null.</summary>
    public int CurrentStepOrder { get; set; }

    /// <summary>Lý do từ chối của lần từ chối gần nhất — bắt buộc phải có khi Status = Rejected. Được xóa khi gửi duyệt lại.</summary>
    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    /// <summary>Tên bước đã từ chối lần gần nhất (VD "Trưởng phòng", "Giám Đốc", "Giám Đốc Nhân sự") — luôn
    /// dùng tên bước chuẩn (không phải tên người được ủy quyền) để giao diện xác định đúng bước bị từ chối
    /// trên thanh tiến trình bất kể ai thực sự bấm nút Từ chối.</summary>
    [MaxLength(50)]
    public string? RejectedByRoleName { get; set; }

    /// <summary>Id người thực sự xử lý (duyệt hoặc từ chối) bước cuối cùng — Giám Đốc Nhân sự hoặc người được
    /// ủy quyền. Null nếu khóa học chưa từng qua bước này (hoặc đã gửi duyệt lại từ đầu sau khi bị từ chối).</summary>
    public int? HrApproverUserId { get; set; }
    public User? HrApprover { get; set; }

    /// <summary>true nếu người xử lý bước cuối là người được ủy quyền (không phải chính Giám Đốc Nhân sự) —
    /// chỉ Giám Đốc Nhân sự mới được xem tên người này (CourseService tự tính khi trả dữ liệu); các vai
    /// trò khác luôn thấy nhãn "Giám Đốc Nhân sự" dù thực tế ai đã xử lý.</summary>
    public bool HrApproverIsDelegate { get; set; }

    /// <summary>Tổng thời lượng dự kiến (phút).</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Điểm tối thiểu (%) để được ghi nhận hoàn thành khóa học.</summary>
    public decimal PassScore { get; set; } = 70m;

    /// <summary>Khóa học bắt buộc với đối tượng được phân công.</summary>
    public bool IsMandatory { get; set; }

    /// <summary>Số ngày hoàn thành kể từ khi được phân công (null = không giới hạn).</summary>
    public int? DeadlineDay { get; set; }

    /// <summary>Ngày khóa học bắt đầu và kết thúc — bắt buộc với mọi khóa học. Chỉ lưu phần ngày (00:00),
    /// EndDate luôn >= StartDate (CourseService kiểm). Khác <see cref="DeadlineDay"/>: hai mốc này là khung thời
    /// gian của cả khóa học, còn DeadlineDay tính hạn riêng cho từng người kể từ lúc họ được phân công.</summary>
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public LearningPath? LearningPath { get; set; }
    public ICollection<Lesson> LessonList { get; set; } = new List<Lesson>();
    public ICollection<Quiz> QuizList { get; set; } = new List<Quiz>();
    public ICollection<ScenarioScene> ScenarioSceneList { get; set; } = new List<ScenarioScene>();
    public ICollection<ScormPackage> ScormPackageList { get; set; } = new List<ScormPackage>();
    public ICollection<CourseTarget> CourseTargetList { get; set; } = new List<CourseTarget>();
    public ICollection<Enrollment> EnrollmentList { get; set; } = new List<Enrollment>();

    // ==================== HÀNH VI: CHUỖI DUYỆT NỘI DUNG ====================
    // Trước đây mỗi bước duyệt được viết tay ở CourseService bằng cách gán Status, lý do từ chối, người duyệt
    // bước cuối và mốc cập nhật — tám chỗ gán rải trong hai chuỗi duyệt. Quên dọn lý do từ chối cũ ở một chỗ là
    // khóa học đã xuất bản vẫn hiện "bị từ chối vì..." mà không có lỗi nào báo. Giờ thực thể tự nắm các bước
    // chuyển và tự từ chối bước chuyển sai trạng thái.
    //
    // Thực thể KHÔNG xét quyền (ai được duyệt bước nào cần tra phòng ban, vai trò, ủy quyền trong database) —
    // việc đó vẫn ở CourseService, và luôn chạy TRƯỚC khi gọi các hàm dưới đây.

    /// <summary>Chỉ gửi duyệt được khóa học đang Nháp hoặc vừa bị Từ chối.</summary>
    public bool CanBeSubmitted => Status is CourseStatus.Draft or CourseStatus.Rejected;

    /// <summary>Đang chờ ở một bước của luồng cấp duyệt do Admin định nghĩa (không phải chuỗi duyệt cố định).</summary>
    public bool IsOnWorkflowStep => Status == CourseStatus.PendingApproval && ApprovalWorkflowId.HasValue;

    /// <summary>Gửi duyệt vào bước đầu tiên của luồng cấp duyệt đã chọn.</summary>
    public void SubmitToWorkflow(int firstStepOrder, DateTime now)
    {
        EnsureCanBeSubmitted();
        ClearPreviousDecision(now);
        Status = CourseStatus.PendingApproval;
        CurrentStepOrder = firstStepOrder;
    }

    /// <summary>Gửi duyệt theo chuỗi cố định Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự.</summary>
    public void SubmitToFixedChain(DateTime now)
    {
        EnsureCanBeSubmitted();
        ClearPreviousDecision(now);
        Status = CourseStatus.PendingHeadApproval;
        CurrentStepOrder = 0;
    }

    /// <summary>Chuỗi cố định, bước 1: Trưởng phòng duyệt.</summary>
    public void ApproveByHeadOfDepartment(DateTime now)
    {
        EnsureStatus(CourseStatus.PendingHeadApproval);
        Status = CourseStatus.PendingDirectorApproval;
        UpdatedAt = now;
    }

    /// <summary>Chuỗi cố định, bước 2: Giám Đốc phòng ban duyệt.</summary>
    public void ApproveByDirector(DateTime now)
    {
        EnsureStatus(CourseStatus.PendingDirectorApproval);
        Status = CourseStatus.PendingHrApproval;
        UpdatedAt = now;
    }

    /// <summary>Chuỗi cố định, bước cuối: Giám Đốc Nhân sự (hoặc người được ủy quyền) duyệt và xuất bản. Ghi rõ ai
    /// thực sự duyệt — chỉ Giám Đốc Nhân sự được xem tên người được ủy quyền.</summary>
    public void PublishByHr(int approverUserId, bool approverIsDelegate, DateTime now)
    {
        EnsureStatus(CourseStatus.PendingHrApproval);
        Status = CourseStatus.Published;
        HrApproverUserId = approverUserId;
        HrApproverIsDelegate = approverIsDelegate;
        UpdatedAt = now;
    }

    /// <summary>Luồng tùy biến: duyệt bước đang chờ. <paramref name="nextStepOrder"/> = null nghĩa là đây là bước
    /// cuối, khóa học được xuất bản.</summary>
    public void ApproveWorkflowStep(int? nextStepOrder, DateTime now)
    {
        EnsureStatus(CourseStatus.PendingApproval);
        if (nextStepOrder is null)
        {
            Status = CourseStatus.Published;
            CurrentStepOrder = 0;
        }
        else
        {
            CurrentStepOrder = nextStepOrder.Value;
        }
        UpdatedAt = now;
    }

    /// <summary>Giảng Viên xuất bản trực tiếp mà không qua chuỗi duyệt. Chỉ từ trạng thái Draft hoặc Rejected.</summary>
    public void PublishDirectly(DateTime now)
    {
        EnsureCanBeSubmitted();
        ClearPreviousDecision(now);
        Status = CourseStatus.Published;
        CurrentStepOrder = 0;
    }

    /// <summary>Từ chối ở bước đang chờ (của bất kỳ chuỗi duyệt nào).
    ///
    /// <paramref name="rejectedByLabel"/> là tên BƯỚC đã từ chối (VD "Giám Đốc Nhân sự" hoặc tên bước của luồng),
    /// không phải tên vai trò của người bấm: thanh tiến trình dò theo tên bước để tô đỏ đúng chỗ.
    /// CurrentStepOrder cố ý giữ nguyên — đó là thứ cho biết khóa học dừng lại ở bước nào.</summary>
    public void Reject(string reason, string rejectedByLabel, DateTime now)
    {
        if (Status is not (CourseStatus.PendingApproval or CourseStatus.PendingHeadApproval
            or CourseStatus.PendingDirectorApproval or CourseStatus.PendingHrApproval))
            throw new InvalidOperationException($"Khóa học không ở bước chờ duyệt nào (hiện: {Status}).");

        Status = CourseStatus.Rejected;
        RejectionReason = reason;
        RejectedByRoleName = rejectedByLabel;
        UpdatedAt = now;
    }

    /// <summary>Ghi nhận người ra quyết định ở bước Nhân sự (dùng khi TỪ CHỐI ở bước đó).</summary>
    public void RecordHrDecisionMaker(int userId, bool isDelegate)
    {
        HrApproverUserId = userId;
        HrApproverIsDelegate = isDelegate;
    }

    /// <summary>Chọn (hoặc bỏ chọn, <paramref name="workflowId"/> = null) luồng cấp duyệt. Chỉ khi khóa học CHƯA nằm
    /// trong chuỗi duyệt: đổi luồng giữa chừng làm CurrentStepOrder trỏ sang một bước khác hẳn của luồng mới.</summary>
    public void ChangeApprovalWorkflow(int? workflowId, DateTime now)
    {
        EnsureCanBeSubmitted();
        ApprovalWorkflowId = workflowId;
        CurrentStepOrder = 0;
        UpdatedAt = now;
    }

    /// <summary>Cất khóa học đi (Lưu trữ).</summary>
    public void Archive(DateTime now)
    {
        Status = CourseStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Đưa về Bản nháp, tức về đầu chuỗi duyệt: bước đang chờ của luồng cấp duyệt xóa theo, không thì
    /// khóa học về Nháp mà thanh tiến trình vẫn đứng ở giữa luồng.</summary>
    public void ReturnToDraft(DateTime now)
    {
        Status = CourseStatus.Draft;
        CurrentStepOrder = 0;
        UpdatedAt = now;
    }

    /// <summary>Gửi lại sau khi bị từ chối: quyết định của vòng trước hết hiệu lực.</summary>
    private void ClearPreviousDecision(DateTime now)
    {
        RejectionReason = null;
        RejectedByRoleName = null;
        HrApproverUserId = null;
        HrApproverIsDelegate = false;
        UpdatedAt = now;
    }

    private void EnsureCanBeSubmitted()
    {
        if (!CanBeSubmitted)
            throw new InvalidOperationException($"Chỉ gửi duyệt được khóa học Nháp hoặc bị Từ chối (hiện: {Status}).");
    }

    private void EnsureStatus(CourseStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Khóa học không ở trạng thái {expected} (hiện: {Status}).");
    }
}
