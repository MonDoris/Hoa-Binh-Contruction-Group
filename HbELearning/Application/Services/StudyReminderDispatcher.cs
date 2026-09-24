// File: Application/Services/StudyReminderDispatcher.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>Bắn các lời nhắc học tập đã tới giờ.</summary>
public interface IStudyReminderDispatcher
{
    /// <summary>Xử lý một lượt: tạo thông báo cho các lời nhắc tới hạn, đẩy mốc lần sau, gửi email kèm theo.</summary>
    Task FireDueRemindersAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Nghiệp vụ bắn lời nhắc học tập người dùng tự đặt (xem <see cref="StudyReminder"/>).
///
/// Tách khỏi StudyReminderWorker ở Infrastructure: vòng hẹn giờ chạy nền là hạ tầng, còn chọn lời nhắc nào tới
/// hạn, bỏ lời nhắc đã quá cũ, soạn nội dung, nhảy sang mốc lặp kế tiếp và gửi email kèm là nghiệp vụ — giờ nằm ở
/// đây, gọi lại được từ bất kỳ đâu chứ không chỉ từ vòng lặp nền.
/// </summary>
public class StudyReminderDispatcher : IStudyReminderDispatcher
{
    /// <summary>Số lời nhắc xử lý trong một lượt quét — chặn trần để một lần khởi động lại sau thời gian dài
    /// tắt máy chủ không kéo cả bảng vào bộ nhớ. Phần còn lại rơi sang lượt quét kế tiếp.</summary>
    private const int BatchSize = 200;

    private readonly IAppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<StudyReminderDispatcher> _logger;

    public StudyReminderDispatcher(IAppDbContext db, IEmailService emailService, ILogger<StudyReminderDispatcher> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task FireDueRemindersAsync(CancellationToken ct)
    {
        var now = DateTime.Now;

        var dueList = await _db.StudyReminder
            .Include(x => x.Enrollment).ThenInclude(x => x!.Course)
            .Where(x => x.IsActive && x.RemindAt <= now)
            .OrderBy(x => x.RemindAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (dueList.Count == 0) return;

        // Việc gửi email gom lại làm sau khi lưu xong: lỗi SMTP không được kéo theo mất bản ghi thông báo.
        var emailJobList = new List<(int UserId, string Title, string Message)>();

        foreach (var reminder in dueList)
        {
            if (!reminder.IsStaleAt(now))
            {
                var message = BuildMessage(reminder);

                _db.Notification.Add(new Notification
                {
                    UserId = reminder.UserId,
                    EnrollmentId = reminder.EnrollmentId,
                    Type = NotificationType.StudyReminder,
                    Title = reminder.Title,
                    Message = message,
                    CreatedAt = now
                });

                reminder.MarkSent(now);
                if (reminder.SendEmail) emailJobList.Add((reminder.UserId, reminder.Title, message));
            }

            reminder.AdvanceAfter(now);
        }

        await _db.SaveChangesAsync(ct);

        if (emailJobList.Count > 0) await SendEmailAsync(emailJobList, ct);
    }

    /// <summary>Nội dung thông báo: ghi chú của người đặt nếu có, không thì câu mặc định kèm tên khóa học.</summary>
    private static string BuildMessage(StudyReminder reminder)
    {
        if (!string.IsNullOrWhiteSpace(reminder.Note)) return reminder.Note;

        var courseTitle = reminder.Enrollment?.Course.Title;
        return string.IsNullOrWhiteSpace(courseTitle)
            ? "Đã tới giờ học bạn tự hẹn. Dành ít phút cho việc học nhé!"
            : $"Đã tới giờ học bạn tự hẹn cho khóa \"{courseTitle}\".";
    }

    /// <summary>Gửi email kèm theo. Không ném lỗi ra ngoài — email chỉ là kênh phụ, thông báo trong ứng dụng đã lưu xong.</summary>
    private async Task SendEmailAsync(List<(int UserId, string Title, string Message)> jobList, CancellationToken ct)
    {
        if (!_emailService.IsConfigured) return;

        var userIdList = jobList.Select(x => x.UserId).Distinct().ToList();
        var emailByUserId = await _db.User.AsNoTracking()
            .Where(x => userIdList.Contains(x.Id))
            .Select(x => new { x.Id, x.Email })
            .ToDictionaryAsync(x => x.Id, x => x.Email, ct);

        foreach (var (userId, title, message) in jobList)
        {
            if (!emailByUserId.TryGetValue(userId, out var email) || string.IsNullOrWhiteSpace(email)) continue;
            try
            {
                await _emailService.SendNotificationAsync(email, title, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gui email loi nhac hoc tap that bai toi {Email}", email);
            }
        }
    }
}
