// File: Application/DTOs/ApprovalWorkflowDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Một bước duyệt trong luồng, kèm tên vai trò / phòng ban đã tra sẵn để giao diện khỏi phải tra lại.</summary>
/// <param name="DepartmentId">null = cùng phòng ban với người tạo khóa học; có giá trị = cố định đúng phòng ban đó.</param>
/// <param name="ApproverMode">Cách chọn người duyệt của bước — xem <see cref="StepApproverMode"/>. Trả về dạng
/// CHỮ ("AnyOfRole"/"PickOne"/"FixedUser") chứ không phải số: giao diện so bằng tên, đọc code là hiểu ngay.</param>
/// <param name="FixedUserId">Người duyệt Admin đã ghim — chỉ khác null khi ApproverMode = FixedUser.</param>
public record ApprovalWorkflowStepDto(
    int Id, int OrderIndex, string Name,
    int RoleId, string RoleName,
    int? DepartmentId, string? DepartmentName,
    bool AllowDelegate,
    string ApproverMode = nameof(StepApproverMode.AnyOfRole),
    int? FixedUserId = null, string? FixedUserName = null);

/// <summary>Luồng cấp duyệt kèm toàn bộ các bước — dùng cho màn hình quản lý của Admin.</summary>
/// <param name="CourseCount">Số khóa học đang gắn với luồng này; khác 0 thì không cho xóa luồng.</param>
/// <param name="DepartmentId">Phòng ban dùng luồng này; null = dùng chung cho mọi phòng ban.</param>
public record ApprovalWorkflowDto(
    int Id, string Name, string? Description, bool IsActive, bool IsDefault,
    int CourseCount, int CreatedById, string CreatedByName, DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<ApprovalWorkflowStepDto> StepList,
    int? DepartmentId = null, string? DepartmentName = null);

/// <summary>Một lựa chọn trong ô xổ xuống "Luồng cấp duyệt" của người soạn nội dung — chỉ những gì cần để
/// chọn và xem trước các bước, không kèm thông tin quản trị.</summary>
/// <param name="DepartmentName">Tên phòng ban của luồng, null = luồng dùng chung. Hiển thị kèm để người soạn
/// phân biệt hai luồng trùng tên ở hai phòng ban.</param>
public record ApprovalWorkflowOptionDto(
    int Id, string Name, string? Description, bool IsDefault, IReadOnlyList<string> StepNameList,
    string? DepartmentName = null);

/// <summary>Một bước gửi lên khi Admin lưu luồng. Thứ tự bước lấy theo VỊ TRÍ trong mảng, không theo trường
/// riêng — giao diện kéo thả/thêm xóa bước liên tục, để phía gọi tự đánh số là chắc chắn có lúc lệch.</summary>
public class ApprovalWorkflowStepRequest
{
    /// <summary>Nhãn hiển thị của bước. Bỏ trống thì server tự điền bằng tên vai trò được giao.</summary>
    [MaxLength(100)]
    public string? Name { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn vai trò duyệt cho từng bước.")]
    public int RoleId { get; set; }

    /// <summary>null = người duyệt phải cùng phòng ban với người tạo khóa học. Không có tác dụng khi
    /// <see cref="ApproverMode"/> = FixedUser (đã ghim đích danh một người thì chính họ là phạm vi).</summary>
    public int? DepartmentId { get; set; }

    public bool AllowDelegate { get; set; }

    /// <summary>Cách chọn người duyệt của bước — xem <see cref="StepApproverMode"/>. Bỏ trống = AnyOfRole
    /// (gửi cho cả nhóm giữ vai trò, hành vi cũ).</summary>
    public StepApproverMode ApproverMode { get; set; } = StepApproverMode.AnyOfRole;

    /// <summary>Người duyệt cố định — BẮT BUỘC khi <see cref="ApproverMode"/> = FixedUser, bỏ qua ở các chế độ khác.</summary>
    public int? FixedUserId { get; set; }
}

public class ApprovalWorkflowRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên luồng cấp duyệt."), MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Phòng ban dùng luồng này; bỏ trống = dùng chung cho mọi phòng ban.</summary>
    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Đặt luồng này làm mặc định — server tự bỏ dấu mặc định ở các luồng còn lại.</summary>
    public bool IsDefault { get; set; }

    public List<ApprovalWorkflowStepRequest> StepList { get; set; } = new();
}

/// <summary>Một vai trò để chọn cho bước duyệt. Tách khỏi /api/role (chỉ Admin gọi được, và trả kèm cả bộ
/// quyền của vai trò): người dựng luồng cấp phòng ban chỉ cần đúng tên vai trò để gán cho một bước.</summary>
public record WorkflowRoleOptionDto(int Id, string Name);

/// <summary>Một người có thể ghim làm người duyệt cố định của bước. Tách khỏi /api/user (chỉ Admin và Trưởng
/// Nhóm gọi được) để Trưởng phòng / Giám Đốc cũng chọn được người duyệt khi dựng luồng của phòng mình.</summary>
public record WorkflowApproverOptionDto(int Id, string FullName, string? DepartmentName, string? PositionName);

/// <summary>Nhân bản một luồng cấp duyệt sang phòng ban khác — thấy luồng của phòng bạn hay thì lấy về áp
/// dụng cho phòng mình, khỏi dựng lại từ đầu.</summary>
public class CloneApprovalWorkflowRequest
{
    /// <summary>Phòng ban đích. Bỏ trống = bản sao dùng chung cho mọi phòng ban.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Tên của bản sao. Bỏ trống thì server tự đặt theo tên luồng gốc kèm tên phòng ban đích.</summary>
    [MaxLength(150)]
    public string? Name { get; set; }
}
