// File: Application/Services/NotificationDispatcher.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

public class NotificationDispatcher : INotificationDispatcher
{
    /// <summary>Giới hạn cột Notification.Message. Lý do từ chối cho phép tới 1000 ký tự mà nội dung thông báo
    /// lại ghép cả lý do vào, nên phải cắt cho vừa — không thì chính thao tác Từ chối đổ vỡ khi lưu thông báo.</summary>
    private const int MessageMaxLength = 500;

    private const int NotStartedAfterDays = 3;
    private const int DueSoonWithinDays = 3;

    /// <summary>Khoảng cách giữa hai lần nhắc định kỳ. Đếm từ lần nhắc gần nhất chứ không neo vào thứ Hai:
    /// thông báo chỉ sinh ra lúc người dùng mở ứng dụng, neo vào ngày cố định sẽ trượt mất tuần nào họ không vào.</summary>
    private const int WeeklyReminderEveryDays = 7;

    private readonly IAppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IAppDbContext db, IEmailService emailService, ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task SyncReminderAsync(int userId)
    {
        var now = DateTime.Now;

        var activeList = await _db.Enrollment.AsNoTracking()
            .Where(x => x.UserId == userId
                && x.Status != EnrollmentStatus.Completed && x.Status != EnrollmentStatus.Cancelled)
            .Select(x => new { x.Id, x.Status, x.AssignedAt, x.DueDate })
            .ToListAsync();

        if (activeList.Count == 0) return;

        var enrollmentIdList = activeList.Select(x => x.Id).ToList();
        var existingTypeSet = (await _db.Notification.AsNoTracking()
                .Where(x => x.UserId == userId && x.EnrollmentId != null && enrollmentIdList.Contains(x.EnrollmentId.Value))
                .Select(x => new { x.EnrollmentId, x.Type })
                .ToListAsync())
            .Select(x => (x.EnrollmentId!.Value, x.Type))
            .ToHashSet();

        var newNotificationList = new List<Notification>();

        foreach (var e in activeList)
        {
            if (e.Status == EnrollmentStatus.Assigned
                && (now - e.AssignedAt).TotalDays >= NotStartedAfterDays
                && !existingTypeSet.Contains((e.Id, NotificationType.NotStarted)))
            {
                newNotificationList.Add(new Notification
                {
                    UserId = userId,
                    EnrollmentId = e.Id,
                    Type = NotificationType.NotStarted,
                    Title = "Nhắc nhở học tập",
                    Message = "Bạn được phân công khóa học này đã vài ngày nhưng chưa bắt đầu học. Hãy dành thời gian hoàn thành nhé!"
                });
            }

            if (e.DueDate.HasValue)
            {
                if (e.DueDate.Value < now && !existingTypeSet.Contains((e.Id, NotificationType.Overdue)))
                {
                    newNotificationList.Add(new Notification
                    {
                        UserId = userId,
                        EnrollmentId = e.Id,
                        Type = NotificationType.Overdue,
                        Title = "Khóa học đã quá hạn",
                        Message = "Khóa học đã quá hạn hoàn thành. Vui lòng hoàn tất sớm nhất có thể."
                    });
                }
                else if (e.DueDate.Value >= now && (e.DueDate.Value - now).TotalDays <= DueSoonWithinDays
                    && !existingTypeSet.Contains((e.Id, NotificationType.DueSoon)))
                {
                    newNotificationList.Add(new Notification
                    {
                        UserId = userId,
                        EnrollmentId = e.Id,
                        Type = NotificationType.DueSoon,
                        Title = "Sắp đến hạn hoàn thành",
                        Message = $"Khóa học sắp đến hạn hoàn thành vào {e.DueDate.Value:dd/MM/yyyy}. Hãy tiếp tục học để hoàn thành đúng hạn."
                    });
                }
            }
        }

        var weekly = await BuildWeeklyReminderAsync(userId, now, activeList.Select(x => x.Id).ToList());
        if (weekly is not null) newNotificationList.Add(weekly);

        if (newNotificationList.Count > 0)
        {
            _db.Notification.AddRange(newNotificationList);
            await _db.SaveChangesAsync();
            await SendReminderEmailAsync(userId, newNotificationList);
        }
    }

    /// <summary>Nhắc nhở định kỳ mỗi tuần: điểm lại số bài học còn dang dở trong các khóa đang học.
    /// Trả về null nếu chưa tới kỳ nhắc hoặc người học đã xong hết bài.</summary>
    private async Task<Notification?> BuildWeeklyReminderAsync(int userId, DateTime now, List<int> enrollmentIdList)
    {
        var lastSentAt = await _db.Notification.AsNoTracking()
            .Where(x => x.UserId == userId && x.Type == NotificationType.WeeklyReminder)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastSentAt.HasValue && (now - lastSentAt.Value).TotalDays < WeeklyReminderEveryDays) return null;

        // Bài học còn dang dở = bài đang mở của khóa chưa xong, trừ những bài đã có LessonProgress ở trạng thái Completed.
        var pendingList = await (
            from e in _db.Enrollment.AsNoTracking()
            where enrollmentIdList.Contains(e.Id)
            join l in _db.Lesson.AsNoTracking() on e.CourseId equals l.CourseId
            where l.IsActive
                && !_db.LessonProgress.Any(p => p.EnrollmentId == e.Id && p.LessonId == l.Id
                    && p.Status == ProgressStatus.Completed)
            select new { e.Id, CourseTitle = e.Course.Title })
            .ToListAsync();

        if (pendingList.Count == 0) return null;

        var courseCount = pendingList.Select(x => x.Id).Distinct().Count();
        var firstCourseTitle = pendingList[0].CourseTitle;

        return new Notification
        {
            UserId = userId,
            // Gắn vào khóa đầu tiên còn dang dở để nút "Học ngay" trên thông báo có chỗ để đi tới.
            EnrollmentId = pendingList[0].Id,
            Type = NotificationType.WeeklyReminder,
            Title = "Nhắc học hằng tuần",
            Message = $"Tuần này bạn còn {pendingList.Count} bài học chưa hoàn thành ở {courseCount} khóa học"
                + (courseCount == 1 ? $" (\"{Truncate(firstCourseTitle, 80)}\")" : "")
                + ". Dành ít phút học tiếp để không bị dồn nhé!"
        };
    }

    /// <summary>Cắt bớt tên khóa học cho vừa Notification.Message (tối đa 500 ký tự).</summary>
    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    /// <summary>Gửi email cho các nhắc nhở vừa tạo. Không ném lỗi ra ngoài — email chỉ là kênh phụ, lỗi gửi không được làm hỏng luồng thông báo trong ứng dụng.</summary>
    private async Task SendReminderEmailAsync(int userId, List<Notification> reminderList)
    {
        if (!_emailService.IsConfigured) return;

        var email = await _db.User.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.Email)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(email)) return;

        foreach (var n in reminderList)
        {
            try
            {
                await _emailService.SendNotificationAsync(email, n.Title, n.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gửi email nhắc nhở học tập thất bại tới {Email}", email);
            }
        }
    }

    public async Task NotifyCourseAssignedAsync(int enrollmentId)
        => await NotifyCourseAssignedAsync(new[] { enrollmentId });

    public async Task NotifyCourseAssignedAsync(IReadOnlyCollection<int> enrollmentIdList)
    {
        if (enrollmentIdList.Count == 0) return;

        var rowList = await _db.Enrollment.AsNoTracking()
            .Where(x => enrollmentIdList.Contains(x.Id))
            .Select(x => new { x.Id, x.UserId, x.DueDate, CourseTitle = x.Course.Title })
            .ToListAsync();

        _db.Notification.AddRange(rowList.Select(x => new Notification
        {
            UserId = x.UserId,
            EnrollmentId = x.Id,
            Type = NotificationType.CourseAssigned,
            Title = "Khóa học mới được giao",
            Message = FitMessage($"Bạn vừa được phân công khóa học \"{x.CourseTitle}\"."
                + (x.DueDate.HasValue ? $" Hạn hoàn thành: {x.DueDate.Value:dd/MM/yyyy}." : ""))
        }));
        await _db.SaveChangesAsync();
    }

    public async Task NotifyCourseWorkflowAsync(int courseId, IReadOnlyCollection<int> recipientUserIdList, NotificationType type, string title, string message)
    {
        var userIdList = recipientUserIdList.Distinct().ToList();
        if (userIdList.Count == 0) return;

        var notificationList = userIdList
            .Select(userId => new Notification { UserId = userId, CourseId = courseId, Type = type, Title = title, Message = message })
            .ToList();

        _db.Notification.AddRange(notificationList);
        await _db.SaveChangesAsync();
        await SendCourseEmailAsync(userIdList, title, message);
    }

    public async Task NotifyCourseRejectedAsync(int courseId, string rejectedByRoleName, string reason)
    {
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return;

        const string title = "Nội dung khóa học bị từ chối";
        var message = FitMessage($"Khóa học \"{course.Title}\" đã bị {rejectedByRoleName} từ chối. Lý do: {reason}");

        _db.Notification.Add(new Notification
        {
            UserId = course.CreatedById,
            CourseId = courseId,
            Type = NotificationType.CourseRejected,
            Title = title,
            Message = message
        });
        await _db.SaveChangesAsync();
        await SendCourseEmailAsync(new[] { course.CreatedById }, title, message);
    }

    public async Task NotifyDelegateRejectedAsync(int courseId, int granterUserId, string delegateName, string reason)
    {
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return;

        const string title = "Người được bạn ủy quyền đã từ chối một khóa học";
        var message = FitMessage($"{delegateName} — người bạn ủy quyền duyệt — đã từ chối khóa học \"{course.Title}\". Lý do: {reason}");

        _db.Notification.Add(new Notification
        {
            UserId = granterUserId,
            CourseId = courseId,
            Type = NotificationType.CourseRejected,
            Title = title,
            Message = message
        });
        await _db.SaveChangesAsync();
        await SendCourseEmailAsync(new[] { granterUserId }, title, message);
    }

    /// <summary>Cắt nội dung thông báo cho vừa cột Message, giữ lại phần đầu (tên khóa học và ai làm gì).</summary>
    private static string FitMessage(string message)
        => message.Length <= MessageMaxLength ? message : message[..(MessageMaxLength - 3)] + "...";

    /// <summary>Gửi email cho một nhóm người dùng — dùng chung cho các bước duyệt nội dung. Không ném lỗi ra ngoài, chỉ ghi log nếu gửi thất bại.</summary>
    private async Task SendCourseEmailAsync(IReadOnlyCollection<int> userIdList, string subject, string bodyText)
    {
        if (!_emailService.IsConfigured) return;

        var emailList = await _db.User.AsNoTracking()
            .Where(x => userIdList.Contains(x.Id))
            .Select(x => x.Email)
            .ToListAsync();

        foreach (var email in emailList)
        {
            if (string.IsNullOrWhiteSpace(email)) continue;
            try
            {
                await _emailService.SendNotificationAsync(email, subject, bodyText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gửi email duyệt nội dung thất bại tới {Email}", email);
            }
        }
    }
}
