// File: Domain/Entities/CourseStepApprover.cs
namespace HbELearning.Domain.Entities;

/// <summary>
/// NGƯỜI DUYỆT ĐƯỢC CHỈ ĐỊNH cho một bước của một khóa học cụ thể — thứ giải bài toán "phòng có hai Trưởng
/// phòng, người soạn muốn gửi đúng một người".
///
/// Chỉ sinh ra cho những bước mà Admin đặt <see cref="Helpers.StepApproverMode.PickOne"/>: khi người soạn
/// bấm Gửi duyệt, họ chọn một người trong danh sách đủ điều kiện của bước (đúng vai trò, đúng phạm vi phòng
/// ban) và lựa chọn đó lưu ở đây. Từ lúc đó CHỈ người này duyệt/từ chối được bước đó, và cũng chỉ mình họ
/// nhận thông báo — thay vì cả nhóm cùng vai trò như bước AnyOfRole.
///
/// Gắn với KHÓA HỌC chứ không phải với luồng: cùng một luồng, mỗi khóa học có thể chỉ định người khác nhau.
/// Toàn bộ bản ghi của một khóa học bị xóa và ghi lại mỗi lần Gửi duyệt (gửi lại sau khi bị từ chối là chạy
/// lại từ bước 1), và bị xóa khi khóa học đổi sang luồng khác — số thứ tự bước của luồng cũ vô nghĩa với luồng mới.
/// </summary>
public class CourseStepApprover
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Bước được chỉ định, khớp ApprovalWorkflowStep.OrderIndex (đánh số từ 1).</summary>
    public int StepOrder { get; set; }

    /// <summary>Người được chọn duyệt bước này.</summary>
    public int ApproverUserId { get; set; }
    public User ApproverUser { get; set; } = null!;

    /// <summary>Người đã chọn — người soạn khi Gửi duyệt, hoặc người đổi lại về sau (Admin / người soạn).</summary>
    public int AssignedById { get; set; }
    public User AssignedBy { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.Now;
}
