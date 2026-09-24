// File: Application/Common/Interfaces/ICurrentUser.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Người đang thực hiện thao tác.
///
/// Trước khi tách lớp, các thuộc tính này nằm trong BaseApiController và đọc thẳng claim của yêu cầu HTTP —
/// nghĩa là nghiệp vụ "ai được làm gì" chỉ chạy được bên trong một Controller. Giờ nghiệp vụ hỏi cổng này; tầng
/// API trả lời bằng claim của thẻ JWT (xem API/Services/CurrentUser.cs).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Id người dùng; 0 nếu chưa đăng nhập.</summary>
    int UserId { get; }

    /// <summary>Tên vai trò THẬT (VD "Trưởng phòng IT") — dùng để ghi nhật ký "ai đã duyệt". Không dùng để phân
    /// quyền: vai trò kế thừa mang nhiều tên, dùng các cờ Is* hoặc <see cref="IsInRole"/>.</summary>
    string Role { get; }

    /// <summary>Họ tên ghi trong thẻ đăng nhập.</summary>
    string? FullName { get; }

    int? DepartmentId { get; }
    int? PositionId { get; }

    bool IsAdmin { get; }
    bool IsTrainer { get; }
    bool IsHeadOfDepartment { get; }
    bool IsDirector { get; }

    /// <summary>Admin hoặc Người soạn bài.</summary>
    bool IsManager { get; }

    /// <summary>Được XEM nội dung đã soạn dù không được sửa: <see cref="IsManager"/> + Trưởng phòng + Giám Đốc.</summary>
    bool IsContentViewer { get; }

    /// <summary>Có mang vai trò này không — tính cả vai trò cha mà vai trò hiện tại kế thừa.</summary>
    bool IsInRole(string roleName);

    /// <summary>Giá trị của một claim tự đặt (xem AppClaimType); null nếu không có.</summary>
    string? FindClaim(string claimType);
}
