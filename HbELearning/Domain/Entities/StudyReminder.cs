// File: Domain/Entities/StudyReminder.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một lời nhắc học tập do CHÍNH người học tự hẹn giờ trên màn hình Lịch học.
///
/// Khác với các nhắc nhở của NotificationDispatcher — vốn được suy ra từ hạn hoàn thành và chỉ sinh ra lúc người
/// dùng mở ứng dụng — lời nhắc này bắn đúng phút người học đặt, do một dịch vụ nền quét theo chu kỳ (xem
/// Application/Services/StudyReminderDispatcher.cs), nên tới giờ là có thông báo dù họ đang không mở trang nào.</summary>
public class StudyReminder
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Khóa học sẽ mở khi bấm vào thông báo. Null = lời nhắc chung, không gắn khóa nào.</summary>
    public int? EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    /// <summary>Việc cần làm, hiện thành tiêu đề thông báo. VD "Học bài An toàn lao động".</summary>
    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(400)]
    public string? Note { get; set; }

    /// <summary>Thời điểm bắn LẦN KẾ TIẾP. Với lời nhắc lặp lại, dịch vụ nền tự đẩy mốc này sang lần sau ngay
    /// khi bắn xong — nên nó luôn là "lần tới", không phải mốc người dùng đặt ban đầu.</summary>
    public DateTime RemindAt { get; set; }

    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.None;

    /// <summary>Gửi kèm email ngoài thông báo trong ứng dụng — dành cho người không mở hệ thống thường xuyên.</summary>
    public bool SendEmail { get; set; }

    /// <summary>Tắt tạm mà không xóa. Lời nhắc một lần cũng tự chuyển về false sau khi đã bắn.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? LastSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ==================== HÀNH VI ====================

    /// <summary>Lời nhắc MỘT LẦN trễ quá ngần này thì bỏ qua, không báo nữa: "đến giờ học 8h sáng" nhận được
    /// sau bốn ngày chỉ là rác. Lời nhắc lặp lại không bị ảnh hưởng — chúng luôn nhảy về mốc sắp tới.</summary>
    public const int StaleAfterDays = 3;

    /// <summary>Đã quá cũ để báo vào thời điểm <paramref name="now"/> (chỉ áp cho lời nhắc một lần).</summary>
    public bool IsStaleAt(DateTime now) =>
        Repeat == ReminderRepeat.None && (now - RemindAt).TotalDays > StaleAfterDays;

    /// <summary>Ghi nhận vừa báo xong lúc <paramref name="now"/>.</summary>
    public void MarkSent(DateTime now) => LastSentAt = now;

    /// <summary>Sau một lượt tới giờ: lời nhắc lặp lại nhảy sang mốc kế tiếp SAU <paramref name="now"/>; lời nhắc một
    /// lần thì tắt — nhưng vẫn giữ bản ghi để người dùng thấy trong danh sách và bật lại được với giờ khác.</summary>
    public void AdvanceAfter(DateTime now)
    {
        var next = ReminderSchedule.Next(RemindAt, Repeat, now);
        if (next.HasValue) RemindAt = next.Value;
        else IsActive = false;
    }
}
