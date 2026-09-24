// File: Domain/Entities/ApprovalWorkflow.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Một LUỒNG CẤP DUYỆT do Admin định nghĩa: danh sách các bước duyệt xếp theo thứ tự, mỗi bước giao cho một
/// vai trò ở một phạm vi phòng ban, và NGƯỜI DUYỆT của từng bước cũng do chính người dựng luồng ghim sẵn
/// (xem <see cref="ApprovalWorkflowStep"/>) — người soạn khóa học không chọn người duyệt cho bài của mình.
///
/// Luồng được PHÂN LOẠI THEO PHÒNG BAN (<see cref="DepartmentId"/>): người duyệt đã ghim đích danh thì mỗi
/// phòng ban cần một luồng riêng với người của phòng đó. Thấy luồng của phòng khác hay thì nhân bản về phòng
/// mình (ApprovalWorkflowService.Clone) chứ không dùng chung một bản.
///
/// Người soạn nội dung chọn một luồng có sẵn cho khóa học của mình (Course.ApprovalWorkflowId); khi bấm
/// "Gửi duyệt", khóa học đi lần lượt qua đúng các bước của luồng đó thay vì chuỗi cố định
/// Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự.
///
/// Khóa học KHÔNG chọn luồng nào (dữ liệu cũ hoặc người tạo bỏ trống) vẫn chạy y nguyên chuỗi cố định cũ —
/// xem CourseService.Submit/Approve/Reject.
/// </summary>
public class ApprovalWorkflow
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Phòng ban dùng luồng này. null = DÙNG CHUNG cho mọi phòng ban (mặc định của dữ liệu cũ và
    /// của những luồng không ghim người duyệt đích danh). Có giá trị = chỉ người soạn thuộc phòng ban đó mới
    /// thấy và chọn được luồng — vì người duyệt ghim trong luồng là người của chính phòng ban đó.</summary>
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Luồng ngừng hoạt động không còn xuất hiện trong ô chọn của người soạn nội dung, nhưng các
    /// khóa học đang chạy dở trên luồng đó vẫn duyệt tiếp bình thường.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Luồng được chọn sẵn trong ô chọn khi người soạn tạo khóa học mới. Mặc định tính THEO TỪNG
    /// PHÒNG BAN: mỗi phòng ban một luồng mặc định, cộng thêm một luồng mặc định dùng chung — không thì đặt
    /// mặc định cho phòng này sẽ gỡ mất mặc định của 38 phòng còn lại.</summary>
    public bool IsDefault { get; set; }

    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<ApprovalWorkflowStep> StepList { get; set; } = new List<ApprovalWorkflowStep>();
}
