// File: Application/DTOs/ApprovalDelegateDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Nhân viên cùng phòng ban với Giám Đốc, đủ điều kiện được ủy quyền duyệt xuất bản khóa học ở bước
/// cuối, kèm cờ cho biết hiện đã được ủy quyền hay chưa.</summary>
public record ApprovalDelegateCandidateDto(int UserId, string FullName, string EmployeeCode, string? AvatarUrl,
    string? PositionName, bool IsDelegated);

/// <summary>Người được ủy quyền duyệt — mỗi Giám Đốc chỉ ủy quyền cho ĐÚNG MỘT người, nên đây là một id chứ
/// không phải danh sách. Bỏ trống (null) nghĩa là thu hồi ủy quyền.</summary>
public class SetApprovalDelegateRequest
{
    public int? UserId { get; set; }
}
