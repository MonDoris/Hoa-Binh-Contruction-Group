// File: Application/Features/StudyReminders/IStudyReminderService.cs

namespace HbELearning.Application.Features.StudyReminders;

/// <summary>Các ca sử dụng của chức năng StudyReminder. Tầng API gọi qua cổng này (xem API/Controllers/StudyReminderService.cs).</summary>
public interface IStudyReminderService
{
    /// <summary>Toàn bộ lời nhắc của người dùng hiện tại — đang bật xếp trước, theo giờ báo gần nhất.</summary>
    Task<Result> GetList();

    Task<Result> Create(StudyReminderRequest request);

    Task<Result> Update(int id, StudyReminderRequest request);

    /// <summary>Bật/tắt một lời nhắc mà không xóa. Tắt rồi bật lại một lời nhắc lặp có giờ đã trôi qua thì mốc
    /// báo được đẩy tới lần kế tiếp, tránh việc vừa bật đã bắn ngay.</summary>
    Task<Result> Toggle(int id);

    Task<Result> Delete(int id);
}
