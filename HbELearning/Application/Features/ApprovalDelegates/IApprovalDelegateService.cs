// File: Application/Features/ApprovalDelegates/IApprovalDelegateService.cs

namespace HbELearning.Application.Features.ApprovalDelegates;

/// <summary>Các ca sử dụng của chức năng ApprovalDelegate. Tầng API gọi qua cổng này (xem API/Controllers/ApprovalDelegateService.cs).</summary>
public interface IApprovalDelegateService
{
    /// <summary>Danh sách nhân viên cùng phòng ban với Giám Đốc để chọn ra một người, kèm cờ cho biết ai đang
    /// được ủy quyền.</summary>
    Task<Result> GetCandidateList();

    /// <summary>Đặt (hoặc thu hồi) người được ủy quyền duyệt. Mỗi Giám Đốc chỉ ủy quyền cho ĐÚNG MỘT người,
    /// và người đó phải đang thuộc cùng phòng ban — gửi lên id ngoài phòng ban thì bị từ chối chứ không âm
    /// thầm bỏ qua, vì với một lựa chọn duy nhất thì "bỏ qua" đồng nghĩa với xóa mất ủy quyền ngoài ý muốn.
    /// Bỏ trống UserId nghĩa là thu hồi ủy quyền hiện có.</summary>
    Task<Result> SetDelegate(SetApprovalDelegateRequest request);
}
