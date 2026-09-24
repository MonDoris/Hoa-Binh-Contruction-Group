// File: Application/Features/Notifications/INotificationService.cs

namespace HbELearning.Application.Features.Notifications;

/// <summary>Các ca sử dụng của chức năng Notification. Tầng API gọi qua cổng này (xem API/Controllers/NotificationService.cs).</summary>
public interface INotificationService
{
    /// <summary>Danh sách thông báo của người dùng hiện tại (mới nhất trước) — tự rà soát nhắc nhở còn thiếu trước khi trả về.</summary>
    Task<Result> GetList();

    /// <summary>Số thông báo chưa đọc — dùng cho huy hiệu trên biểu tượng chuông.</summary>
    Task<Result> GetUnreadCount();

    Task<Result> MarkAsRead(int id);

    Task<Result> MarkAllAsRead();
}
