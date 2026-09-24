// File: Application/Features/Progress/ProgressService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Progress;

public class ProgressService : IProgressService
{
    private readonly IAppDbContext _db;
    private readonly ILearningService _learningService;
    private readonly IRankingEngine _rankingService;

    /// <summary>Chặn trên cho một nhịp heartbeat (giây). Nhịp thật cách nhau 30 giây; nhận một con số lớn
    /// hơn nhiều nghĩa là máy vừa ngủ dậy, tab bị treo lâu, hoặc ai đó gọi thẳng API để tự tăng giờ học.
    /// Kẹp lại thay vì từ chối: người học ngồi học thật trong lúc máy lag không đáng bị mất tiến độ.</summary>
    private const int MaxHeartbeatSecond = 120;
    private readonly ICurrentUser _currentUser;

    public ProgressService(IAppDbContext db, ILearningService learningService, IRankingEngine rankingService, ICurrentUser currentUser)
    {
        _db = db;
        _learningService = learningService;
        _rankingService = rankingService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetPath(int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (!_currentUser.IsManager && enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        return Result.Success(await _learningService.BuildPathProgressAsync(enrollmentId));
    }

    public async Task<Result> StartLesson(int enrollmentId, int lessonId)
    {
        var check = await ValidateAsync(enrollmentId, lessonId);
        if (check is not null) return check;

        var progress = await _db.LessonProgress
            .FirstOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.LessonId == lessonId);

        if (progress is null)
        {
            progress = new LessonProgress
            {
                EnrollmentId = enrollmentId,
                LessonId = lessonId,
                Status = ProgressStatus.InProgress,
                StartedAt = DateTime.Now
            };
            _db.LessonProgress.Add(progress);
        }
        else if (progress.Status == ProgressStatus.NotStarted)
        {
            progress.Status = ProgressStatus.InProgress;
            progress.StartedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        await _learningService.RecalculateAsync(enrollmentId);

        return Result.Success(new MessageResponse("Đã ghi nhận bắt đầu học."));
    }

    public async Task<Result> Heartbeat(int enrollmentId, int lessonId, int timeSpentSecond = 0, int lastPositionSecond = 0, int maxWatchedSecond = 0)
    {
        var check = await ValidateAsync(enrollmentId, lessonId);
        if (check is not null) return check;

        var progress = await _db.LessonProgress
            .FirstOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.LessonId == lessonId);
        if (progress is null) return Result.Invalid(new MessageResponse("Chưa bắt đầu bài học này."));

        var addedSecond = Math.Clamp(timeSpentSecond, 0, MaxHeartbeatSecond);
        progress.TimeSpentSecond += addedSecond;
        if (lastPositionSecond > 0) progress.LastPositionSecond = lastPositionSecond;
        // Chỉ cho tăng: đây là mốc xa nhất ĐÃ xem, dùng để khóa tua ở bài "bắt buộc xem hết". Nếu ghi đè theo
        // giá trị frontend gửi lên thì chỉ cần tua lùi rồi chờ một nhịp heartbeat là mốc bị kéo tụt xuống,
        // học viên xem hết video rồi vẫn bị chặn tua như chưa xem.
        if (maxWatchedSecond > progress.MaxWatchedSecond) progress.MaxWatchedSecond = maxWatchedSecond;

        await _db.SaveChangesAsync();
        await TrackDailyStudyAsync(addedSecond);

        return Result.Success(new MessageResponse("Đã cập nhật tiến độ."));
    }

    /// <summary>Cộng dồn thời gian học của NGÀY HÔM NAY và trao điểm khi vừa chạm ngưỡng quản trị viên đặt.
    ///
    /// Phải đếm riêng theo ngày vì LessonProgress.TimeSpentSecond cộng dồn theo từng bài, không mang ngày
    /// tháng — nhìn vào đó không trả lời được "hôm nay học bao nhiêu phút".
    ///
    /// Nuốt lỗi có chủ đích, cùng lý do với điểm đăng nhập: đây là phần thưởng phụ, hỏng nó không được phép
    /// làm hỏng việc ghi tiến độ học — thứ mà mất là học viên phải xem lại từ đầu.</summary>
    private async Task TrackDailyStudyAsync(int addedSecond)
    {
        if (addedSecond <= 0) return;

        try
        {
            var today = DateTime.Today;
            var tally = await _db.UserDailyStudy
                .FirstOrDefaultAsync(x => x.UserId == _currentUser.UserId && x.StudyDate == today);

            if (tally is null)
            {
                tally = new UserDailyStudy { UserId = _currentUser.UserId, StudyDate = today };
                _db.UserDailyStudy.Add(tally);
            }

            tally.StudySecond += addedSecond;
            await _db.SaveChangesAsync();

            // Ngưỡng null = quản trị viên đã tắt hoạt động này (hoặc loại này vốn không dùng ngưỡng).
            var thresholdMinute = await _rankingService.GetThresholdAsync(PointReason.DailyStudyTime);
            if (thresholdMinute is null or <= 0) return;
            if (tally.StudySecond < thresholdMinute * 60) return;

            // Gọi mỗi nhịp heartbeat sau khi đã đủ giờ cũng chỉ cộng một lần: nguồn của hoạt động này là
            // chính ngày hôm nay, và nhật ký điểm có ràng buộc duy nhất trên (người, lý do, nguồn).
            await _rankingService.AwardDailyPointsAsync(_currentUser.UserId, PointReason.DailyStudyTime, today);
        }
        catch { /* bỏ qua: không để phần thưởng làm hỏng việc ghi tiến độ */ }
    }

    public async Task<Result> CompleteLesson(int enrollmentId, int lessonId)
    {
        var check = await ValidateAsync(enrollmentId, lessonId);
        if (check is not null) return check;

        var progress = await _db.LessonProgress
            .FirstOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.LessonId == lessonId);

        if (progress is null)
        {
            progress = new LessonProgress
            {
                EnrollmentId = enrollmentId,
                LessonId = lessonId,
                StartedAt = DateTime.Now
            };
            _db.LessonProgress.Add(progress);
        }

        progress.MarkCompleted(DateTime.Now);

        await _db.SaveChangesAsync();
        await _learningService.RecalculateAsync(enrollmentId);

        var enrollment = await _db.Enrollment.AsNoTracking().FirstAsync(x => x.Id == enrollmentId);
        return Result.Success(new MessageResponse($"Đã hoàn thành bài học. Tiến độ khóa học: {enrollment.ProgressPercent}%."));
    }

    /// <summary>Kiểm tra quyền truy cập + trạng thái mở khóa của bài học trong lộ trình tuần tự.</summary>
    private async Task<Result?> ValidateAsync(int enrollmentId, int lessonId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        if (enrollment.Status == EnrollmentStatus.Cancelled)
            return Result.Invalid(new MessageResponse("Khóa học này đã bị hủy phân công."));

        var pathList = await _learningService.BuildPathProgressAsync(enrollmentId);
        var step = pathList.FirstOrDefault(x => x.LessonId == lessonId);

        if (step is null) return Result.Invalid(new MessageResponse("Bài học không thuộc lộ trình của khóa học này."));
        if (!step.IsUnlocked) return Result.Invalid(new MessageResponse("Bạn cần hoàn thành các bước trước đó trong lộ trình."));

        return null;
    }
}
