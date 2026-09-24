// File: Domain/Entities/ApprovalWorkflowStep.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một bước trong luồng cấp duyệt: ai (vai trò) duyệt, ở phạm vi phòng ban nào, đứng thứ mấy.</summary>
public class ApprovalWorkflowStep
{
    public int Id { get; set; }

    public int ApprovalWorkflowId { get; set; }
    public ApprovalWorkflow ApprovalWorkflow { get; set; } = null!;

    /// <summary>Thứ tự bước, đánh số từ 1. Course.CurrentStepOrder trỏ thẳng vào giá trị này.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Nhãn hiển thị của bước trên thanh tiến trình (VD "Trưởng phòng duyệt"). Bỏ trống thì
    /// ApprovalWorkflowService tự điền bằng tên vai trò được giao.</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Vai trò được duyệt bước này. Vai trò kế thừa cũng khớp: người giữ "Trưởng phòng IT" duyệt
    /// được bước giao cho "Trưởng phòng" (nhưng không ngược lại) — xem CourseService.CanActOnStep.</summary>
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Phạm vi phòng ban của người duyệt:
    /// null = CÙNG phòng ban với người tạo khóa học (bước cấp trên trực tiếp, VD Trưởng phòng của chính phòng đó);
    /// có giá trị = đúng phòng ban này bất kể khóa học của phòng nào (VD bước cuối luôn do phòng Nhân sự duyệt).</summary>
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Cách xác định NGƯỜI duyệt bước này (xem <see cref="StepApproverMode"/>):
    /// AnyOfRole = gửi cho cả nhóm giữ vai trò, ai bấm trước cũng được (mặc định, hành vi cũ);
    /// PickOne = người soạn chỉ định đúng một người lúc Gửi duyệt;
    /// FixedUser = Admin ghim sẵn một người ở <see cref="FixedUserId"/>.</summary>
    public StepApproverMode ApproverMode { get; set; } = StepApproverMode.AnyOfRole;

    /// <summary>Người duyệt cố định của bước — CHỈ có ý nghĩa khi <see cref="ApproverMode"/> = FixedUser.
    /// Đã ghim đích danh một người thì <see cref="DepartmentId"/> không còn tác dụng: chính người đó là phạm
    /// vi rồi. Cố ý KHÔNG kèm khóa ngoại (cột được thêm vào bảng đã có dữ liệu) — ApprovalWorkflowService
    /// kiểm người này còn hoạt động và đúng vai trò của bước mỗi lần lưu luồng.</summary>
    public int? FixedUserId { get; set; }

    /// <summary>Cho phép người đã được ủy quyền duyệt (bảng ApprovalDelegate) bấm Duyệt/Từ chối thay ở bước
    /// này, dù họ không giữ vai trò được giao. Đúng bằng cơ chế ủy quyền sẵn có của bước Giám Đốc Nhân sự.</summary>
    public bool AllowDelegate { get; set; }
}
