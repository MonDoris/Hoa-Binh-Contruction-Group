// File: Application/DTOs/RoleDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>
/// Dạng rút gọn, dùng cho các ô chọn vai trò (form tạo/sửa người dùng).
///
/// Có kèm FunctionKeys vì biểu mẫu người dùng phải vẽ sẵn quyền của vai trò vừa chọn: quyền cấp riêng
/// cho cá nhân chỉ GIAO với quyền vai trò (xem Helpers/FunctionKey.Effective), nên tick một quyền nằm
/// ngoài vai trò là vô tác dụng — giao diện cần biết để khóa lại thay vì để Admin tick rồi tưởng đã cấp.
/// </summary>
/// <param name="ParentRoleId">Vai trò cha được kế thừa — ô chọn vai trò dựa vào đây để lồng con dưới cha.</param>
/// <param name="FunctionKeys">Quyền của vai trò, ĐÃ cộng sẵn quyền nhận từ cha. Màn hình Người dùng dùng nó
/// làm trần cho phần cấp quyền riêng, nên phải là quyền thực tế chứ không phải phần tick riêng của vai trò.</param>
public record RoleDto(int Id, string Name, string? Description, string? FunctionKeys = null,
    int? ParentRoleId = null, string? ParentRoleName = null);

/// <summary>Một dòng trong màn hình quản lý vai trò.</summary>
/// <param name="ParentRoleId">Vai trò cha được kế thừa (luôn là một vai trò hệ thống), null = đứng riêng.</param>
/// <param name="ParentRoleName">Tên vai trò cha — để danh sách hiện cây mà không phải tra chéo.</param>
/// <param name="InheritedFunctionKeys">Quyền NHẬN TỪ CHA. Giao diện dùng để hiện các ô quyền đã có sẵn ở dạng
/// khóa cứng: bỏ tick chúng cũng không có tác dụng gì, vì lúc đăng nhập quyền cha luôn được cộng vào.</param>
public record RoleListDto(
    int Id, string Name, string? Description, string? FunctionKeys,
    bool IsSystem, bool IsActive, int UserCount,
    int? ParentRoleId = null, string? ParentRoleName = null, string? InheritedFunctionKeys = null);

/// <summary>Một chức năng có thể phân quyền, kèm các thao tác của nó — để giao diện dựng bảng tick quyền.</summary>
public record FunctionDto(string Key, string[] ActionList);

public class RoleSaveRequest
{
    [Required(ErrorMessage = "Tên vai trò là bắt buộc"), MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(255)] public string? Description { get; set; }

    /// <summary>Chuỗi CSV các khóa quyền, VD "user.view,user.create". Null = không cấp quyền nào.</summary>
    [MaxLength(2000)] public string? FunctionKeys { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Vai trò cha để kế thừa. Phải là một vai trò HỆ THỐNG (chỉ một cấp) — xem RoleService.</summary>
    public int? ParentRoleId { get; set; }
}
