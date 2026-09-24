// File: Domain/Constants/RoleName.cs
namespace HbELearning.Domain.Constants;

/// <summary>Hằng số tên vai trò, dùng cho [Authorize(Roles = ...)].</summary>
public static class RoleName
{
    public const string Admin            = "Admin";          // Quản trị hệ thống
    public const string ManagingDirector = "Tổng Giám Đốc";  // Quản lý giám đốc, báo cáo & xếp hạng toàn công ty
    public const string Director         = "Giám Đốc";       // Quản lý theo phòng ban, duyệt nội dung do Trưởng phòng đưa lên
    public const string HeadOfDepartment = "Trưởng phòng";   // Duyệt nội dung do Trưởng Nhóm tạo, báo cáo theo phòng ban
    public const string Trainer          = "Trưởng Nhóm";    // Tạo nội dung bài học, phụ trách đào tạo (tên hằng số giữ nguyên để không phải sửa các Controller đang dùng)
    public const string Learner          = "Học viên";       // Nhân viên / người học (tên hằng số giữ nguyên để không phải sửa các Controller đang dùng)

    public const string AdminOrTrainer = Admin + "," + Trainer;

    /// <summary>Được soạn nội dung khóa học (tạo/sửa khóa học, bài học, bài kiểm tra, mô phỏng 3D, lộ trình,
    /// phân công học viên, nhập Excel, nhờ AI viết nội dung): CHỈ Trưởng Nhóm.
    ///
    /// Admin cố ý nằm ngoài: Admin quản trị hệ thống (người dùng, phòng ban, vai trò, xếp hạng...) chứ không
    /// tham gia chuỗi soạn - duyệt nội dung. Trên khóa học, Admin chỉ được XEM, LƯU TRỮ và XÓA — hai việc sau
    /// vẫn giữ [Authorize(Roles = Admin)] riêng ở ChangeStatus/Delete của CourseService.
    ///
    /// Để Admin tạo/sửa nội dung thì nội dung đó không đi qua chuỗi duyệt của phòng ban nào cả: Submit gửi
    /// thông báo theo phòng ban của NGƯỜI TẠO, mà Admin thường không thuộc phòng ban nào.</summary>
    public const string ContentAuthor = Trainer;

    /// <summary>Các cấp tham gia duyệt nội dung bài học: Trưởng phòng và Giám Đốc (phòng ban lẫn Giám Đốc Nhân sự).</summary>
    public const string ApprovalRoles = HeadOfDepartment + "," + Director;

    // ==================== LUỒNG CẤP DUYỆT ====================
    // Luồng cấp duyệt không còn là việc riêng của Admin: mỗi phòng ban tự dựng luồng của mình, vì người duyệt
    // ghim trong luồng là nhân sự của chính phòng đó — Admin ngồi ngoài mọi phòng ban thì không biết ai duyệt ai.

    /// <summary>CẤP PHÒNG BAN — được dựng luồng cấp duyệt cho CHÍNH phòng ban của mình (tạo/sửa/xóa):
    /// Trưởng Nhóm, Trưởng phòng, Giám Đốc. Phạm vi phòng ban do server ép theo phòng ban của người đăng
    /// nhập, không lấy theo dữ liệu gửi lên — xem ApprovalWorkflowService.</summary>
    public const string WorkflowDepartmentAuthor = Trainer + "," + HeadOfDepartment + "," + Director;

    /// <summary>CẤP CÔNG TY — được xem luồng của các phòng ban KHÁC để tham khảo và nhân bản về phòng mình:
    /// Trưởng phòng và Giám Đốc. Trưởng Nhóm cố ý nằm ngoài: họ dựng luồng trong phòng mình, việc tham khảo
    /// cách làm của phòng khác là quyết định của cấp quản lý phòng ban.</summary>
    public const string WorkflowCompanyViewer = HeadOfDepartment + "," + Director;

    /// <summary>Được xem báo cáo/thống kê đào tạo toàn công ty: Admin, Trưởng Nhóm và Tổng Giám Đốc.</summary>
    public const string ReportViewers = AdminOrTrainer + "," + ManagingDirector;

    /// <summary>Được xem chi tiết khóa học (kể cả danh sách học viên đã phân công) dù không có quyền tạo/sửa nội dung:
    /// Admin, Trưởng Nhóm (người tạo), và các cấp duyệt Trưởng phòng/Giám Đốc — khớp Auth.require ở course-detail.html.</summary>
    public const string CourseViewers = AdminOrTrainer + "," + ApprovalRoles;

    /// <summary>Mọi vai trò TRỪ Admin — dùng cho tạo/sửa/hủy phòng học online. Admin chỉ quản trị hệ thống
    /// (quản lý người dùng/phòng ban/xếp hạng...), không trực tiếp tổ chức buổi học.</summary>
    public const string AllExceptAdmin = Trainer + "," + Learner + "," + HeadOfDepartment + "," + Director + "," + ManagingDirector;

    // ==================== THANG BẬC QUẢN LÝ ====================

    /// <summary>Bậc quản lý của một vai trò hệ thống. Số lớn hơn = cấp cao hơn.
    ///
    /// Dùng để trả lời câu hỏi "ai là cấp dưới của ai" — thứ hệ thống chưa bao giờ lưu thành một liên kết
    /// người-quản-lý-người, mà suy ra từ VAI TRÒ cộng với PHÒNG BAN (xem <see cref="IsCompanyWide"/>).
    ///
    /// Vai trò do Admin tự tạo ("Trưởng phòng IT") KHÔNG có tên trong bảng này: chỗ gọi phải quy nó về vai
    /// trò cha trước (Role.ParentRoleId) rồi mới hỏi bậc — đúng cách token cấp claim vai trò cho nó.
    /// Tên lạ hoàn toàn trả về 0, tức là không quản lý ai — chọn 0 chứ không ném lỗi vì một vai trò rời rạc
    /// do Admin tạo mà quên gán cha vẫn phải dùng được hệ thống bình thường.</summary>
    public static int RankOf(string? roleName) => roleName switch
    {
        Admin => 100,
        ManagingDirector => 90,
        Director => 70,
        HeadOfDepartment => 50,
        Trainer => 30,
        Learner => 10,
        _ => 0
    };

    /// <summary>Vai trò này quản lý TOÀN CÔNG TY hay chỉ trong phòng ban của mình.
    ///
    /// Admin và Tổng Giám Đốc nhìn xuyên phòng ban; Giám Đốc và Trưởng phòng thì không — đó đã là ranh giới
    /// mà RankingService và CourseService đang áp, ở đây chỉ nói lại cho rõ thành một chỗ.</summary>
    public static bool IsCompanyWide(string? roleName) =>
        roleName is Admin or ManagingDirector;
}
