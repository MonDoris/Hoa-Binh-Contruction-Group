// File: Application/Features/Courses/ICourseService.cs

namespace HbELearning.Application.Features.Courses;

/// <summary>Các ca sử dụng của chức năng Course. Tầng API gọi qua cổng này (xem API/Controllers/CourseService.cs).</summary>
public interface ICourseService
{
    /// <summary>Danh sách khóa học. Learner chỉ thấy khóa đã xuất bản.</summary>
    Task<Result> GetList(string? keyword, CourseStatus? status, int? departmentId, int? positionId, int page = 1, int pageSize = 20);

    Task<Result> GetById(int id);

    /// <summary>Khóa học phù hợp phòng ban / vị trí của tôi mà tôi chưa được phân công.</summary>
    Task<Result> GetMyAvailable();

    Task<Result> Create(CourseCreateRequest request);

    Task<Result> Update(int id, CourseRequest request);

    /// <summary>Đổi luồng cấp duyệt của một khóa học — để người soạn chọn ngay trên thanh tiến trình ở danh sách,
    /// không phải mở biểu mẫu sửa. Tách thành action riêng thay vì dùng Update: CourseRequest đòi đủ tên, thời
    /// lượng, điểm đạt... nên một lần đổi luồng qua đó sẽ ghi đè luôn những trường mà giao diện không gửi lên.
    /// workflowId = null nghĩa là bỏ chọn, khóa học quay về chuỗi duyệt cố định.</summary>
    Task<Result> ChangeApprovalWorkflow(int id, int? workflowId);

    /// <summary>Ghi đè trạng thái thủ công (VD: lưu trữ khóa học đã xuất bản). Không dùng để xuất bản trực
    /// tiếp — xuất bản phải đi qua chuỗi duyệt Trưởng Nhóm -> Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự.</summary>
    Task<Result> ChangeStatus(int id, CourseStatus status);

    /// <summary>Trưởng Nhóm gửi khóa học Nháp của chính mình đi duyệt.
    ///
    /// Người soạn KHÔNG chỉ định người duyệt: người duyệt từng bước do người dựng luồng ghim sẵn trong định
    /// nghĩa luồng (ApprovalWorkflowStep.FixedUserId), bước không ghim ai thì gửi cho cả nhóm giữ vai trò
    /// trong phạm vi phòng ban của bước. Vì vậy yêu cầu này không có thân.</summary>
    Task<Result> Submit(int id);

    /// <summary>
    /// Duyệt khóa học lên cấp kế tiếp trong chuỗi. Trưởng phòng duyệt khóa học cùng phòng ban đang chờ
    /// mình; Giám Đốc duyệt khóa học cùng phòng ban đang chờ Giám Đốc phòng ban, hoặc nếu là Giám Đốc
    /// của phòng Nhân sự thì duyệt luôn bước cuối (mọi phòng ban) để xuất bản.
    /// </summary>
    Task<Result> Approve(int id);

    /// <summary>Từ chối nội dung đang chờ duyệt ở cấp của mình — bắt buộc nhập lý do, chuyển khóa học sang trạng thái Từ chối để
    /// Trưởng Nhóm sửa lại và gửi duyệt lại. Người tạo được báo trạng thái + lý do qua chuông thông báo và Email.</summary>
    Task<Result> Reject(int id, CourseRejectRequest request);

    /// <summary>Lịch sử duyệt (lộ trình qua các người duyệt) của một khóa học, mới nhất trước. Không giới hạn theo
    /// vai trò ở tầng route — quyền xem cụ thể do CanViewCourseAsync quyết định (cho phép cả người được ủy quyền
    /// duyệt bước cuối, có thể không giữ vai trò nào trong RoleName.CourseViewers).</summary>
    Task<Result> GetApprovalHistory(int id);

    /// <summary>Các bước của luồng cấp duyệt gắn với khóa học, kèm danh sách người có thể duyệt từng bước —
    /// đổ vào hộp thoại "Đổi người duyệt".
    ///
    /// CHỈ Admin: người soạn không còn chọn người duyệt cho bài của mình (việc đó thuộc về người dựng luồng),
    /// mà đây lại là danh bạ nhân sự theo phòng ban — không phải thứ để ai đọc được khóa học cũng tra ra.</summary>
    Task<Result> GetStepApproverOptionList(int id);

    /// <summary>Đổi người duyệt của một bước khi khóa học đang chờ duyệt — người được chỉ định nghỉ phép,
    /// nghỉ việc hoặc chọn nhầm. Không có cửa này thì khóa học nằm chờ một người không bao giờ bấm nữa.
    ///
    /// CHỈ Admin — người soạn không can thiệp được vào chuỗi duyệt bài của chính mình, kể cả bằng đường này.
    /// Chỉ đổi được bước đang chờ và các bước phía sau: bước đã duyệt xong mà đổi người là viết lại lịch sử.</summary>
    Task<Result> ReassignApprover(int id, CourseReassignApproverRequest request);

    Task<Result> Delete(int id, int userId, bool isAdmin);

    Task<Result> AddTarget(int id, CourseTargetRequest request);

    Task<Result> RemoveTarget(int targetId);
}
