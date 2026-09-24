// File: Application/Features/Dashboard/DashboardService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Dashboard;

/// <summary>Số liệu riêng của trang Tổng quan. Tách khỏi ReportService vì đó là báo cáo quản trị (nhìn
/// toàn công ty), còn đây là số liệu của CHÍNH người đang đăng nhập.</summary>
public class DashboardService : IDashboardService
{
    /// <summary>Số ngày hiện trên dải chuỗi học. 12 ngày vừa đủ nhìn thấy thói quen gần hai tuần mà vẫn
    /// xếp gọn một hàng trên màn hình hẹp.</summary>
    private const int StreakDayCount = 12;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DashboardService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetLearnerDashboard(CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var weekStart = WeekHelper.StartOfWeek(today);

        // Lấy đủ dữ liệu của cả dải ngày lẫn cả tuần trong MỘT truy vấn: hai mốc này không trùng nhau (tuần
        // có thể bắt đầu sau ngày đầu dải), nên lấy theo mốc sớm hơn rồi lọc trong bộ nhớ.
        var fromDate = today.AddDays(-(StreakDayCount - 1));
        if (weekStart < fromDate) fromDate = weekStart;

        var logList = await _db.UserPointLog.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId && x.CreatedAt >= fromDate)
            .Select(x => new { x.CreatedAt, x.Reason, x.Points })
            .ToListAsync(cancellationToken);

        var activeDaySet = logList.Select(x => x.CreatedAt.Date).ToHashSet();

        // ----- Dải 12 ngày gần nhất -----
        var dayList = Enumerable.Range(0, StreakDayCount)
            .Select(offset => today.AddDays(offset - (StreakDayCount - 1)))
            .Select(date => new StreakDayDto(date, WeekdayLabel(date), activeDaySet.Contains(date), date == today))
            .ToList();

        // ----- Nhiệm vụ tuần này -----
        // Đề bài do quản trị viên đặt (xem phần QUẢN TRỊ bên dưới); chỉ lấy nhiệm vụ đang bật — nhiệm vụ
        // tắt vẫn còn trong bảng nhưng người học không thấy.
        var weekLogList = logList.Where(x => x.CreatedAt >= weekStart).ToList();
        var missionRowList = await _db.WeeklyMission.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var missionList = missionRowList
            .Select(mission => new WeeklyMissionDto(
                $"{mission.Reason}-{mission.Id}",
                mission.Label,
                weekLogList.Count(x => x.Reason == mission.Reason),
                mission.Target,
                mission.RewardPoint))
            .ToList();

        var totalPoint = await _db.UserPointLog.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId)
            .SumAsync(x => (int?)x.Points, cancellationToken) ?? 0;

        return Result.Success(new LearnerDashboardDto(
            CountStreakDay(activeDaySet, today),
            weekLogList.Sum(x => x.Points),
            totalPoint,
            dayList,
            missionList));
    }

    /// <summary>Đếm số ngày học liên tiếp tính ngược từ hôm nay.
    ///
    /// Hôm nay chưa học thì KHÔNG coi là đứt chuỗi mà đếm tiếp từ hôm qua: người học còn nguyên phần ngày
    /// còn lại để giữ chuỗi, báo "chuỗi 0 ngày" ngay từ sáng vừa sai vừa làm mất động lực. Chuỗi chỉ thật
    /// sự đứt khi cả hôm qua lẫn hôm nay đều trống.</summary>
    private static int CountStreakDay(HashSet<DateTime> activeDaySet, DateTime today)
    {
        var cursor = activeDaySet.Contains(today) ? today : today.AddDays(-1);

        var count = 0;
        while (activeDaySet.Contains(cursor))
        {
            count++;
            cursor = cursor.AddDays(-1);
        }
        return count;
    }

    private static string WeekdayLabel(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "T2",
        DayOfWeek.Tuesday => "T3",
        DayOfWeek.Wednesday => "T4",
        DayOfWeek.Thursday => "T5",
        DayOfWeek.Friday => "T6",
        DayOfWeek.Saturday => "T7",
        _ => "CN",
    };

    public async Task<Result> GetMissionList(CancellationToken cancellationToken)
    {
        var list = await _db.WeeklyMission.AsNoTracking()
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return Result.Success(list.Select(x => new WeeklyMissionDetailDto(
                x.Id, x.Label, x.Reason.ToString(), x.Target, x.RewardPoint, x.OrderIndex, x.IsActive))
            .ToList());
    }

    public async Task<Result> GetMissionActivityList()
    {
        var pointMap = await GetPointMapAsync();
        return Result.Success(Enum.GetValues<PointReason>()
            .Where(IsLearningActivity)
            .Select(x => new MissionActivityDto((int)x, x.ToString(), pointMap.GetValueOrDefault(x)))
            .ToList());
    }

    /// <summary>Bảng điểm đang áp dụng, đọc từ HBE_POINT_RULE và bù bằng mức khởi điểm cho loại hoạt động
    /// chưa có dòng nào (VD loại mới thêm trong bản cập nhật, chạy trước khi seeder kịp gieo).</summary>
    private async Task<Dictionary<PointReason, int>> GetPointMapAsync()
    {
        var ruleList = await _db.PointRule.AsNoTracking().ToListAsync();
        return Enum.GetValues<PointReason>()
            .Where(IsLearningActivity)
            .ToDictionary(x => x, x => ruleList.FirstOrDefault(r => r.Reason == x)?.Points
                                       ?? RankingEngine.DefaultPointTable.GetValueOrDefault(x));
    }

    public async Task<Result> GetPointRuleList()
    {
        var ruleList = await _db.PointRule.AsNoTracking().ToListAsync();

        return Result.Success(Enum.GetValues<PointReason>()
            .Where(IsLearningActivity)
            .Select(x =>
            {
                var rule = ruleList.FirstOrDefault(r => r.Reason == x);
                // Loại nào VỐN có ngưỡng thì luôn trả về một con số, kể cả khi dòng trong DB đang để trống:
                // trả null làm giao diện tưởng loại này không cần ngưỡng và giấu luôn ô nhập, quản trị viên
                // hết đường đặt lại.
                var threshold = rule?.Threshold
                    ?? (RankingEngine.DefaultThresholdTable.TryGetValue(x, out var d) ? d : (int?)null);
                return new PointRuleDto((int)x, x.ToString(),
                    rule?.Points ?? RankingEngine.DefaultPointTable.GetValueOrDefault(x), threshold);
            })
            .ToList());
    }

    public async Task<Result> SavePointRuleList(PointRuleSaveRequest request)
    {
        var ruleList = await _db.PointRule.ToListAsync();

        foreach (var item in request.ItemList)
        {
            var reason = (PointReason)item.Reason;
            if (!IsLearningActivity(reason))
                return Result.Invalid(new MessageResponse("Loại hoạt động không hợp lệ."));

            // Chỉ nhận ngưỡng cho loại hoạt động thật sự dùng tới nó — gửi kèm ngưỡng cho "đăng nhập
            // trong ngày" là dữ liệu vô nghĩa, giữ lại chỉ để gây hiểu nhầm cho người đọc bảng sau này.
            var threshold = RankingEngine.DefaultThresholdTable.ContainsKey(reason) ? item.Threshold : null;

            var rule = ruleList.FirstOrDefault(x => x.Reason == reason);
            if (rule is null)
            {
                _db.PointRule.Add(new PointRule { Reason = reason, Points = item.Point, Threshold = threshold });
                continue;
            }

            if (rule.Points == item.Point && rule.Threshold == threshold) continue;
            rule.Points = item.Point;
            rule.Threshold = threshold;
            rule.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu bảng điểm hoạt động. Mức mới áp dụng cho những lần cộng điểm từ giờ trở đi."));
    }

    private static bool IsLearningActivity(PointReason reason) =>
        Enum.IsDefined(reason) && reason != PointReason.WeeklyMissionCompleted;

    public async Task<Result> CreateMission(WeeklyMissionRequest request)
    {
        if (!IsLearningActivity(request.Reason))
            return Result.Invalid(new MessageResponse("Loại hoạt động không hợp lệ."));

        _db.WeeklyMission.Add(new WeeklyMission
        {
            Label = request.Label.Trim(),
            Reason = request.Reason,
            Target = request.Target,
            RewardPoint = request.RewardPoint,
            OrderIndex = request.OrderIndex,
            IsActive = request.IsActive,
        });
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã thêm nhiệm vụ tuần."));
    }

    public async Task<Result> UpdateMission(int id, WeeklyMissionRequest request)
    {
        if (!IsLearningActivity(request.Reason))
            return Result.Invalid(new MessageResponse("Loại hoạt động không hợp lệ."));

        var entity = await _db.WeeklyMission.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy nhiệm vụ."));

        entity.Label = request.Label.Trim();
        entity.Reason = request.Reason;
        entity.Target = request.Target;
        entity.RewardPoint = request.RewardPoint;
        entity.OrderIndex = request.OrderIndex;
        entity.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã cập nhật nhiệm vụ tuần."));
    }

    public async Task<Result> DeleteMission(int id)
    {
        var entity = await _db.WeeklyMission.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy nhiệm vụ."));

        _db.WeeklyMission.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa nhiệm vụ tuần."));
    }
}
