// File: Application/Services/INotificationDispatcher.cs

namespace HbELearning.Application.Services;

public interface INotificationDispatcher
{
    /// <summary>Rà soát các khóa học đang phân công của một người dùng, tạo nhắc nhở còn thiếu (chưa bắt đầu / sắp đến hạn / quá hạn).</summary>
    Task SyncReminderAsync(int userId);

    /// <summary>Tạo thông báo "khóa học mới được giao" cho một bản ghi phân công.</summary>
    Task NotifyCourseAssignedAsync(int enrollmentId);

    /// <summary>Thông báo một bước trong chuỗi duyệt nội dung (gửi duyệt / duyệt lên cấp kế tiếp / xuất bản) cho danh sách người nhận — kèm Email.</summary>
    Task NotifyCourseWorkflowAsync(int courseId, IReadOnlyCollection<int> recipientUserIdList, NotificationType type, string title, string message);

    /// <summary>Thông báo từ chối nội dung khóa học cho người tạo, kèm lý do từ chối bắt buộc từ vai trò đã từ chối — kèm Email.</summary>
    Task NotifyCourseRejectedAsync(int courseId, string rejectedByRoleName, string reason);

    /// <summary>Báo riêng cho người ĐÃ ỦY QUYỀN biết người mình ủy quyền vừa từ chối khóa học nào, vì lý do gì.
    /// Thông báo từ chối thường chỉ tới người soạn, nên không có dòng này thì người ủy quyền không hề hay biết
    /// bài đã bị trả về dưới danh nghĩa mình.</summary>
    Task NotifyDelegateRejectedAsync(int courseId, int granterUserId, string delegateName, string reason);
}
