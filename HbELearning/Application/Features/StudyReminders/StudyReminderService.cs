// File: Application/Features/StudyReminders/StudyReminderService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.StudyReminders;

/// <summary>Lời nhắc học tập do người học TỰ hẹn giờ trên màn hình Lịch học.
///
/// Mọi thao tác ở đây chỉ chạm tới lời nhắc của chính người đang đăng nhập — không có đường nào cho người khác
/// đặt hay xem lời nhắc hộ, kể cả quản trị viên. Việc bắn thông báo đúng giờ do StudyReminderWorker lo.</summary>
public class StudyReminderService : IStudyReminderService
{
    /// <summary>Trần số lời nhắc còn hiệu lực của một người. Đủ rộng cho lịch học cá nhân, mà vẫn chặn được
    /// việc một tài khoản tự đẩy hàng nghìn dòng vào vòng quét nền.</summary>
    private const int MaxActivePerUser = 50;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public StudyReminderService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList()
    {
        var list = await _db.StudyReminder.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.RemindAt)
            .Select(x => new StudyReminderDto(
                x.Id, x.Title, x.Note, x.RemindAt, x.Repeat.ToString(),
                x.SendEmail, x.IsActive, x.LastSentAt,
                x.EnrollmentId,
                x.Enrollment != null ? x.Enrollment.CourseId : (int?)null,
                x.Enrollment != null ? x.Enrollment.Course.Title : null))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> Create(StudyReminderRequest request)
    {
        var error = await ValidateAsync(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var activeCount = await _db.StudyReminder.CountAsync(x => x.UserId == _currentUser.UserId && x.IsActive);
        if (activeCount >= MaxActivePerUser)
            return Result.Invalid(new MessageResponse($"Bạn đã có {MaxActivePerUser} lời nhắc đang bật. Hãy xóa bớt trước khi đặt thêm."));

        var reminder = new StudyReminder
        {
            UserId = _currentUser.UserId,
            Title = request.Title.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RemindAt = NormalizeRemindAt(request.RemindAt, request.Repeat),
            Repeat = request.Repeat,
            SendEmail = request.SendEmail,
            EnrollmentId = request.EnrollmentId
        };

        _db.StudyReminder.Add(reminder);
        await _db.SaveChangesAsync();

        return Result.Success(await ProjectAsync(reminder.Id));
    }

    public async Task<Result> Update(int id, StudyReminderRequest request)
    {
        var reminder = await _db.StudyReminder.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _currentUser.UserId);
        if (reminder is null) return Result.NotFound(new MessageResponse("Không tìm thấy lời nhắc."));

        var error = await ValidateAsync(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        reminder.Title = request.Title.Trim();
        reminder.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        reminder.RemindAt = NormalizeRemindAt(request.RemindAt, request.Repeat);
        reminder.Repeat = request.Repeat;
        reminder.SendEmail = request.SendEmail;
        reminder.EnrollmentId = request.EnrollmentId;
        // Sửa giờ cho một lời nhắc đã báo xong (và đã tự tắt) chính là ý muốn hẹn lại — bật lên luôn, để người
        // dùng không phải bấm thêm một nút "bật" nữa mới thấy nó chạy.
        reminder.IsActive = true;

        await _db.SaveChangesAsync();
        return Result.Success(await ProjectAsync(reminder.Id));
    }

    public async Task<Result> Toggle(int id)
    {
        var reminder = await _db.StudyReminder.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _currentUser.UserId);
        if (reminder is null) return Result.NotFound(new MessageResponse("Không tìm thấy lời nhắc."));

        reminder.IsActive = !reminder.IsActive;

        var now = DateTime.Now;
        if (reminder.IsActive && reminder.RemindAt <= now)
        {
            var next = ReminderSchedule.Next(reminder.RemindAt, reminder.Repeat, now);
            if (next.HasValue) reminder.RemindAt = next.Value;
            else return Result.Invalid(new MessageResponse("Giờ hẹn đã trôi qua. Hãy sửa lại giờ trước khi bật lời nhắc này."));
        }

        await _db.SaveChangesAsync();
        return Result.Success(await ProjectAsync(reminder.Id));
    }

    public async Task<Result> Delete(int id)
    {
        var reminder = await _db.StudyReminder.FirstOrDefaultAsync(x => x.Id == id && x.UserId == _currentUser.UserId);
        if (reminder is null) return Result.NotFound(new MessageResponse("Không tìm thấy lời nhắc."));

        _db.StudyReminder.Remove(reminder);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa lời nhắc."));
    }

    /// <summary>Kiểm tra dữ liệu gửi lên. Trả câu báo lỗi, null nghĩa là hợp lệ.</summary>
    private async Task<string?> ValidateAsync(StudyReminderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return "Hãy đặt tên cho lời nhắc.";
        if (!Enum.IsDefined(request.Repeat)) return "Chu kỳ lặp không hợp lệ.";

        // Lời nhắc MỘT LẦN đặt vào quá khứ thì không bao giờ hữu ích — chặn ngay để người dùng biết mà sửa,
        // thay vì lưu xong rồi lặng lẽ bị vòng quét nền bỏ qua vì quá cũ. Lời nhắc lặp lại thì không sao:
        // NormalizeRemindAt đẩy nó tới lần kế tiếp.
        if (request.Repeat == ReminderRepeat.None && request.RemindAt <= DateTime.Now)
            return "Giờ nhắc phải nằm ở tương lai.";

        // Không tin EnrollmentId gửi lên: người khác cũng đoán được số, mà thông báo tạo ra sẽ dẫn thẳng vào
        // khóa học đó ở màn hình học bài.
        if (request.EnrollmentId.HasValue)
        {
            var isMine = await _db.Enrollment.AnyAsync(x =>
                x.Id == request.EnrollmentId.Value && x.UserId == _currentUser.UserId);
            if (!isMine) return "Khóa học gắn với lời nhắc không thuộc về bạn.";
        }

        return null;
    }

    /// <summary>Giờ báo thực sự đem đi lưu: giờ người dùng chọn, hoặc lần lặp kế tiếp nếu giờ đó đã trôi qua.
    /// "Nhắc 8h hằng ngày" đặt lúc 9h sáng phải hiểu là bắt đầu từ 8h sáng MAI, không phải bắn ngay lập tức.</summary>
    private static DateTime NormalizeRemindAt(DateTime remindAt, ReminderRepeat repeat)
    {
        // Giây/mili giây thừa từ trình duyệt chỉ làm lệch mốc so với giờ người dùng thấy trên màn hình.
        remindAt = new DateTime(remindAt.Year, remindAt.Month, remindAt.Day, remindAt.Hour, remindAt.Minute, 0);

        var now = DateTime.Now;
        if (remindAt > now || repeat == ReminderRepeat.None) return remindAt;

        return ReminderSchedule.Next(remindAt, repeat, now) ?? remindAt;
    }

    private async Task<StudyReminderDto> ProjectAsync(int id) =>
        await _db.StudyReminder.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new StudyReminderDto(
                x.Id, x.Title, x.Note, x.RemindAt, x.Repeat.ToString(),
                x.SendEmail, x.IsActive, x.LastSentAt,
                x.EnrollmentId,
                x.Enrollment != null ? x.Enrollment.CourseId : (int?)null,
                x.Enrollment != null ? x.Enrollment.Course.Title : null))
            .FirstAsync();
}
