// File: Application/Features/Notifications/NotificationService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Notifications;

/// <summary>Thông báo và nhắc nhở học tập cho người dùng hiện tại.</summary>
public class NotificationService : INotificationService
{
    private readonly IAppDbContext _db;
    private readonly INotificationDispatcher _notificationService;
    private readonly ICurrentUser _currentUser;

    public NotificationService(IAppDbContext db, INotificationDispatcher notificationService, ICurrentUser currentUser)
    {
        _db = db;
        _notificationService = notificationService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList()
    {
        await _notificationService.SyncReminderAsync(_currentUser.UserId);

        var list = await _db.Notification.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new NotificationDto(
                x.Id, x.Type.ToString(), x.Title, x.Message, x.IsRead, x.CreatedAt,
                x.EnrollmentId,
                x.CourseId ?? (x.Enrollment != null ? x.Enrollment.CourseId : (int?)null),
                x.Course != null ? x.Course.Title : (x.Enrollment != null ? x.Enrollment.Course.Title : null)))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetUnreadCount()
    {
        await _notificationService.SyncReminderAsync(_currentUser.UserId);

        var count = await _db.Notification.CountAsync(x => x.UserId == _currentUser.UserId && !x.IsRead);
        return Result.Success(new UnreadCountDto(count));
    }

    public async Task<Result> MarkAsRead(int id)
    {
        var notification = await _db.Notification.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _currentUser.UserId);
        if (notification is null) return Result.NotFound(new MessageResponse("Không tìm thấy thông báo."));

        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã đánh dấu đã đọc."));
    }

    public async Task<Result> MarkAllAsRead()
    {
        var list = await _db.Notification.Where(x => x.UserId == _currentUser.UserId && !x.IsRead).ToListAsync();
        foreach (var n in list) n.IsRead = true;
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã đánh dấu tất cả đã đọc."));
    }
}
