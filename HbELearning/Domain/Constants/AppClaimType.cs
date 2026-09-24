// File: Domain/Constants/AppClaimType.cs

namespace HbELearning.Domain.Constants;

/// <summary>
/// Tên các claim tự đặt trong thẻ JWT của hệ thống.
///
/// Đặt ở Domain vì hai lớp KHÁC NHAU phải thống nhất từng chữ một: Infrastructure là nơi GHI claim vào thẻ
/// (xem TokenService), còn API là nơi ĐỌC claim ra để xét quyền (xem PermissionAuthorizationHandler).
/// Trước đây hằng số nằm trong chính lớp handler, khiến nơi phát hành thẻ phải với ngược lên lớp ngoài cùng
/// — đúng chiều bị Clean Architecture cấm. Gõ sai một ký tự ở đây thì mọi claim lặng lẽ không khớp mà không
/// có lỗi biên dịch nào, nên hai bên buộc phải dùng chung đúng một nguồn.
/// </summary>
public static class AppClaimType
{
    /// <summary>Danh sách khóa chức năng mà vai trò của người dùng được phép dùng.</summary>
    public const string PermissionClaimType = "perm";

    /// <summary>"true" = vai trò cài sẵn của hệ thống, "false" = vai trò do Admin tự tạo.</summary>
    public const string SystemRoleClaimType = "isSystemRole";
}
