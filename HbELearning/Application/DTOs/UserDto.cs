// File: Application/DTOs/UserDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record UserProfileDto(
    int Id, string EmployeeCode, string FullName, string Email, string RoleName,
    int? DepartmentId, string? DepartmentName,
    int? PositionId, string? PositionName, string? AvatarUrl,
    string? PhoneNumber, DateTime CreatedAt, DateTime? LastLoginAt,
    bool IsApprovalDelegate = false,
    string? FunctionKeys = null,
    string? RoleFunctionKeys = null,
    /// <summary>Tên vai trò CHA nếu vai trò của người này kế thừa (VD "Trưởng phòng IT" -> "Trưởng phòng").
    /// Giao diện phải xét cả tên này khi quyết định hiện menu nào và cho vào trang nào, y như server xét
    /// claim vai trò thứ hai trong token.</summary>
    string? ParentRoleName = null,
    /// <summary>Vai trò của người này (hoặc vai trò cha mà nó kế thừa) đang được giao ít nhất một bước trong một
    /// LUỒNG CẤP DUYỆT còn hoạt động. Họ phải vào được màn hình khóa học để duyệt, dù vai trò đó không nằm trong
    /// danh sách quản lý khóa học thông thường — giống cách IsApprovalDelegate mở đường cho người được ủy quyền duyệt thay.</summary>
    bool IsWorkflowApprover = false,
    /// <summary>Tên tài khoản đăng nhập thay cho email — null nếu tài khoản chưa đặt. Xem <see cref="User.UserName"/>.</summary>
    string? UserName = null);

public record UserListDto(
    int Id, string EmployeeCode, string FullName, string Email, string RoleName,
    string? DepartmentName, string? PositionName, bool IsActive, DateTime CreatedAt,
    /// <summary>Tài khoản đang bị khóa vì gõ sai mật khẩu quá số lần cho phép — xem <see cref="User.LockedOutAt"/>.
    /// Khác hẳn <see cref="IsActive"/>: người này vẫn đang làm việc, chỉ là không tự vào được nữa cho tới khi
    /// phòng IT mở khóa.</summary>
    bool IsLockedOut = false);

public class UserCreateRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = null!;
    [Required, EmailAddress, MaxLength(150)] public string Email { get; set; } = null!;
    /// <summary>Tên tài khoản đăng nhập (tùy chọn). Bỏ trống thì người này chỉ đăng nhập bằng email.
    /// Dạng thức do UserNameHelper quy định — xem <see cref="User.UserName"/>.</summary>
    [MaxLength(50)] public string? UserName { get; set; }
    /// <summary>Quy tắc đầy đủ ở <see cref="PasswordPolicy"/>, kiểm tại UserService.Create.</summary>
    [Required] public string Password { get; set; } = null!;
    [Required] public int RoleId { get; set; }
    public int? DepartmentId { get; set; }
    public int? PositionId { get; set; }
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    /// <summary>Đường dẫn ảnh đại diện đã tải lên trước đó (qua /api/upload?subFolder=avatar). Admin chọn
    /// ảnh ngay trên biểu mẫu thêm người dùng, nên tài khoản mới có ảnh từ đầu chứ không phải đợi chính
    /// người đó đăng nhập rồi tự đổi.</summary>
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    [MaxLength(1000)] public string? FunctionKeys { get; set; }
}

public class UserUpdateRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = null!;
    /// <summary>Tên tài khoản đăng nhập — để trống là gỡ bỏ, người này quay về chỉ đăng nhập bằng email.</summary>
    [MaxLength(50)] public string? UserName { get; set; }
    [Required] public int RoleId { get; set; }
    public int? DepartmentId { get; set; }
    public int? PositionId { get; set; }
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(1000)] public string? FunctionKeys { get; set; }
}

/// <summary>Admin đặt lại mật khẩu cho người dùng khác — khác với DTOs/AuthDto.cs ResetPasswordRequest (luồng quên mật khẩu tự phục vụ qua OTP).</summary>
public class AdminResetPasswordRequest
{
    /// <summary>Quy tắc đầy đủ ở <see cref="PasswordPolicy"/>, kiểm tại UserService.ResetPassword.</summary>
    [Required] public string NewPassword { get; set; } = null!;
}

/// <summary>Tự cập nhật hồ sơ cá nhân — không cho đổi vai trò / phòng ban / vị trí (chỉ Admin mới được).</summary>
public class UpdateProfileRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = null!;
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
}
