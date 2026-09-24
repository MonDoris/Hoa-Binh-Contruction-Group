// File: Domain/Entities/Role.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Role
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(255)]
    public string? Description { get; set; }

    /// <summary>
    /// Quyền mặc định của vai trò — chuỗi CSV các khóa "chức năng.thao tác" (VD "user.view,user.create").
    /// Null = chưa cấu hình, hệ thống dùng lại quyền mặc định gắn cứng theo tên vai trò (hành vi cũ).
    /// Quyền riêng của từng người (User.FunctionKeys) luôn được GIAO với quyền vai trò, không mở rộng thêm.
    /// </summary>
    [MaxLength(2000)]
    public string? FunctionKeys { get; set; }

    /// <summary>
    /// Vai trò lõi do hệ thống cài sẵn (Admin, Học viên...). Không cho đổi tên và không cho xóa vì
    /// tên của chúng được dùng trực tiếp trong luồng duyệt khóa học và các [Authorize(Roles = ...)].
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Vai trò CHA mà vai trò này kế thừa — luôn là một trong sáu vai trò hệ thống, hoặc null nếu đứng riêng.
    ///
    /// Sinh ra để giải bài toán "Trưởng phòng IT", "Trưởng phòng Nhân sự", "Giám Đốc IT"...: về bản chất chúng
    /// LÀ Trưởng phòng / Giám Đốc, chỉ khác phạm vi phòng ban. Trước khi có trường này, một vai trò tự tạo phải
    /// tick lại từ đầu toàn bộ ô quyền, mà vẫn không vào được những chỗ hệ thống kiểm theo TÊN vai trò (menu,
    /// chuỗi duyệt khóa học) — nên "Giám Đốc IT" không duyệt nổi bài của chính phòng mình.
    ///
    /// Có cha thì vai trò con:
    ///   * cộng thêm toàn bộ quyền chức năng của cha (xem RoleService.ResolveEffectiveKeysAsync),
    ///   * và MANG LUÔN danh tính của cha khi hệ thống kiểm tra vai trò — token cấp thêm một claim vai trò
    ///     bằng tên cha (xem TokenService), nên [Authorize(Roles = "Trưởng phòng")] và IsHeadOfDepartment
    ///     đều nhận.
    ///
    /// Quyền duyệt nội dung đi kèm danh tính đó KHÔNG mở rộng phạm vi: chuỗi duyệt vốn đã chặn
    /// "phải cùng phòng ban với người tạo" (xem CourseService.Approve), nên Trưởng phòng IT chỉ duyệt
    /// được bài của phòng IT — đúng như tên gọi của vai trò.
    ///
    /// CHỈ MỘT CẤP: cha bắt buộc là vai trò hệ thống, nên không bao giờ có chuỗi kế thừa dài hay vòng lặp
    /// (A là cha B, B là cha A). Ràng buộc này được kiểm ở RoleService.
    /// </summary>
    public int? ParentRoleId { get; set; }
    public Role? ParentRole { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<User> UserList { get; set; } = new List<User>();
}
