// File: Application/Services/RankingEngine.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>
/// Hệ thống xếp hạng (Ranking) theo điểm tích lũy: mỗi hoạt động học tập cộng 1 mốc điểm cố định,
/// tổng điểm quyết định bậc hạng (Bronze/Silver/...) do quản trị viên cấu hình. Khi đạt bậc mới,
/// tự động cấp 1 mã voucher còn trống trong kho của bậc đó (nếu có) và gửi thông báo cho người học.
/// </summary>
public class RankingEngine : IRankingEngine
{
    /// <summary>Mức điểm KHỞI ĐIỂM cho mỗi hoạt động học — chỉ dùng để gieo bảng HBE_POINT_RULE lần đầu
    /// (xem DbSeeder) và làm mức dự phòng khi bảng đó thiếu dòng cho một loại hoạt động mới thêm.
    ///
    /// Số thực sự đang áp dụng nằm trong DB do quản trị viên đặt, KHÔNG phải bảng này — sửa mấy con số dưới
    /// đây sẽ không đổi được gì trên hệ thống đã chạy. Muốn đổi thì vào màn hình Xếp hạng → Điểm hoạt động.
    ///
    /// KHÔNG chứa PointReason.WeeklyMissionCompleted: điểm thưởng nhiệm vụ do quản trị viên đặt riêng cho
    /// từng nhiệm vụ (WeeklyMission.RewardPoint) nên không có mốc cố định nào để khai ở đây. Vì vậy phải
    /// tra bảng này bằng GetValueOrDefault, đừng dùng chỉ mục [reason].</summary>
    public static readonly IReadOnlyDictionary<PointReason, int> DefaultPointTable = new Dictionary<PointReason, int>
    {
        [PointReason.LessonCompleted] = 5,
        [PointReason.QuizPassed] = 10,
        [PointReason.CourseCompleted] = 30,
        [PointReason.SimulationCompleted] = 15,
        // Ba loại dưới đây thưởng cho THÓI QUEN chứ không cho khối lượng học, nên mức thấp hơn hẳn: mở
        // phần mềm ra mỗi ngày là việc đáng khuyến khích nhưng không thể đáng bằng học xong một bài.
        [PointReason.DailyLogin] = 2,
        [PointReason.DailyStudyTime] = 3,
        [PointReason.VideoQuestionPassed] = 2,
    };

    /// <summary>Ngưỡng khởi điểm cho những hoạt động cần đo lường. Cũng như bảng điểm, đây chỉ là mức gieo
    /// lần đầu — số đang áp dụng nằm trong DB do quản trị viên đặt.</summary>
    public static readonly IReadOnlyDictionary<PointReason, int> DefaultThresholdTable = new Dictionary<PointReason, int>
    {
        [PointReason.DailyStudyTime] = 10,   // phút
    };

    private readonly IAppDbContext _db;

    public RankingEngine(IAppDbContext db) => _db = db;

    /// <summary>Số điểm hiện đang áp dụng cho một hoạt động, lấy từ bảng quản trị viên đặt.
    ///
    /// Thiếu dòng thì rơi về mức khởi điểm thay vì cộng 0: bảng bị xóa nhầm hay loại hoạt động mới thêm mà
    /// quên gieo dữ liệu thì hệ thống vẫn cộng điểm như cũ, chứ không âm thầm ngừng cộng cho cả công ty —
    /// đó là loại hỏng không ai phát hiện ra cho tới lúc học viên thắc mắc sao học mãi không lên hạng.</summary>
    private async Task<int> GetPointForAsync(PointReason reason)
    {
        var rule = await _db.PointRule.AsNoTracking().FirstOrDefaultAsync(x => x.Reason == reason);
        return rule?.Points ?? DefaultPointTable.GetValueOrDefault(reason);
    }

    /// <summary>Nguồn của những hoạt động tính theo ngày. Ngày được nén thành số yyyyMMdd để nhét vừa cột
    /// SourceId kiểu int — cùng cách ràng buộc duy nhất của nhật ký điểm vẫn dùng cho mọi hoạt động khác,
    /// nên không phải thêm bảng hay cột nào để chống cộng trùng.</summary>
    public const string DailySourceType = "Day";

    private static int DayToSourceId(DateTime day) => day.Year * 10000 + day.Month * 100 + day.Day;

    public Task AwardDailyPointsAsync(int userId, PointReason reason, DateTime day) =>
        AwardPointsAsync(userId, reason, DailySourceType, DayToSourceId(day));

    public async Task<int?> GetThresholdAsync(PointReason reason)
    {
        var rule = await _db.PointRule.AsNoTracking().FirstOrDefaultAsync(x => x.Reason == reason);
        if (rule is not null) return rule.Threshold;
        return DefaultThresholdTable.TryGetValue(reason, out var fallback) ? fallback : null;
    }

    public async Task AwardPointsAsync(int userId, PointReason reason, string sourceType, int sourceId)
    {
        var alreadyAwarded = await _db.UserPointLog.AnyAsync(x =>
            x.UserId == userId && x.Reason == reason && x.SourceType == sourceType && x.SourceId == sourceId);
        if (alreadyAwarded) return;

        // Admin không tham gia xếp hạng nên cũng không cộng điểm: cộng rồi giấu khỏi bảng thì họ vẫn leo bậc
        // hạng và ĂN MẤT một mã voucher thật của kho thưởng (xem CheckAndAwardTierAsync), lại còn nhận thông
        // báo "Chúc mừng bạn đã đạt hạng". Chặn ngay từ gốc thì mọi nhánh phía sau — nhiệm vụ tuần, bậc
        // hạng, chuỗi ngày học — đều tự khỏi tính.
        //
        // Đặt SAU phép kiểm cộng trùng ở trên chứ không đặt lên đầu hàm: nhịp heartbeat gọi vào đây bốn lần
        // mỗi phút cho mỗi người đang học, mà những lần đó đều dừng ngay ở dòng trên.
        if (await IsAdminAsync(userId)) return;

        _db.UserPointLog.Add(new UserPointLog
        {
            UserId = userId,
            // Ghi số điểm CỤ THỂ vào nhật ký chứ không tham chiếu tới bảng quy tắc: quản trị viên hạ điểm
            // một hoạt động thì những lần đã cộng phải giữ nguyên, nếu không tổng điểm cả công ty tụt theo
            // và có người rơi khỏi bậc hạng đã được trao.
            Points = await GetPointForAsync(reason),
            Reason = reason,
            SourceType = sourceType,
            SourceId = sourceId,
        });
        await _db.SaveChangesAsync();

        // Xét nhiệm vụ TRƯỚC khi xét bậc hạng: điểm thưởng nhiệm vụ cũng tính vào tổng điểm, nên cộng xong
        // rồi mới xét bậc thì cú hoàn thành nhiệm vụ có thể đẩy người học lên hạng ngay trong cùng lượt.
        await CheckAndAwardMissionAsync(userId, reason);
        await CheckAndAwardTierAsync(userId);
    }

    /// <summary>Cộng điểm thưởng cho những nhiệm vụ tuần vừa đủ chỉ tiêu nhờ hoạt động vừa rồi.
    ///
    /// Chạy ngay lúc cộng điểm hoạt động chứ không đợi người học mở trang Tổng quan: điểm thưởng phải vào
    /// tài khoản dù họ có xem hay không, và bảng xếp hạng phải đúng ngay cả với người chưa bao giờ mở
    /// trang đó.
    ///
    /// Mỗi (người, nhiệm vụ, tuần) chỉ thưởng một lần — ràng buộc duy nhất của HBE_USER_POINT_LOG lo phần
    /// này, xem <see cref="WeekHelper.MissionSourceType"/>. Hệ quả cần biết: quản trị viên hạ chỉ tiêu
    /// xuống dưới số người học đã làm được thì họ nhận thưởng ở LẦN HOẠT ĐỘNG KẾ TIẾP trong tuần, chứ
    /// không phải ngay lúc chỉ tiêu vừa hạ — không có công việc nền nào quét lại toàn bộ người học.</summary>
    private async Task CheckAndAwardMissionAsync(int userId, PointReason reason)
    {
        // Nhiệm vụ không kèm thưởng (RewardPoint = 0) thì bỏ qua: thêm một dòng nhật ký 0 điểm kèm một
        // thông báo "được cộng 0 điểm" chỉ làm rác hộp thông báo.
        var missionList = await _db.WeeklyMission
            .Where(x => x.IsActive && x.Reason == reason && x.RewardPoint > 0)
            .ToListAsync();
        if (missionList.Count == 0) return;

        var weekStart = WeekHelper.StartOfWeek(DateTime.Today);
        var sourceType = WeekHelper.MissionSourceType(weekStart);

        // Số lần đã làm hoạt động này trong tuần. Dòng thưởng nhiệm vụ mang Reason riêng nên không tự đếm
        // chính nó vào đây.
        var doneCount = await _db.UserPointLog
            .CountAsync(x => x.UserId == userId && x.Reason == reason && x.CreatedAt >= weekStart);

        var awardedMissionIdSet = (await _db.UserPointLog
            .Where(x => x.UserId == userId
                        && x.Reason == PointReason.WeeklyMissionCompleted
                        && x.SourceType == sourceType)
            .Select(x => x.SourceId)
            .ToListAsync()).ToHashSet();

        var hasNewAward = false;
        foreach (var mission in missionList)
        {
            if (doneCount < mission.Target || awardedMissionIdSet.Contains(mission.Id)) continue;
            hasNewAward = true;

            _db.UserPointLog.Add(new UserPointLog
            {
                UserId = userId,
                Points = mission.RewardPoint,
                Reason = PointReason.WeeklyMissionCompleted,
                SourceType = sourceType,
                SourceId = mission.Id,
            });

            _db.Notification.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.WeeklyMissionDone,
                Title = $"Hoàn thành nhiệm vụ tuần: {mission.Label}",
                Message = $"Bạn được thưởng {mission.RewardPoint} điểm.",
            });
        }

        if (hasNewAward) await _db.SaveChangesAsync();
    }

    /// <summary>Kiểm tra tổng điểm hiện tại so với các bậc hạng chưa đạt, cấp bậc + mã voucher (nếu còn) + thông báo cho từng bậc mới đủ điều kiện.</summary>
    private async Task CheckAndAwardTierAsync(int userId)
    {
        var totalPoints = await _db.UserPointLog.Where(x => x.UserId == userId).SumAsync(x => x.Points);

        var awardedTierIdSet = (await _db.UserRankAward
            .Where(x => x.UserId == userId)
            .Select(x => x.RankTierId)
            .ToListAsync()).ToHashSet();

        var newlyQualifiedTierList = await _db.RankTier
            .Where(x => x.MinPoints <= totalPoints)
            .OrderBy(x => x.MinPoints)
            .ToListAsync();

        var hasNewAward = false;
        foreach (var tier in newlyQualifiedTierList)
        {
            if (awardedTierIdSet.Contains(tier.Id)) continue;
            hasNewAward = true;

            var code = await _db.RankVoucherCode
                .Where(x => x.RankTierId == tier.Id && !x.IsUsed)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
            if (code is not null)
            {
                code.IsUsed = true;
                code.AssignedToUserId = userId;
                code.AssignedAt = DateTime.Now;
            }

            _db.UserRankAward.Add(new UserRankAward
            {
                UserId = userId,
                RankTierId = tier.Id,
                VoucherCodeId = code?.Id,
            });

            _db.Notification.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.RankUp,
                Title = $"{tier.Icon} Chúc mừng bạn đã đạt hạng {tier.Name}!",
                Message = string.IsNullOrWhiteSpace(tier.RewardDescription)
                    ? $"Bạn vừa đạt hạng {tier.Name} với {totalPoints} điểm tích lũy."
                    : code is not null
                        ? $"Phần thưởng: {tier.RewardDescription}. Mã của bạn: {code.Code}"
                        : $"Phần thưởng: {tier.RewardDescription}. Kho mã đang hết, quản trị viên sẽ bổ sung và cấp mã cho bạn sau.",
            });
        }

        if (hasNewAward) await _db.SaveChangesAsync();
    }

    public async Task<RankStatusDto> GetStatusAsync(int userId)
    {
        var totalPoints = await _db.UserPointLog.Where(x => x.UserId == userId).SumAsync(x => (int?)x.Points) ?? 0;

        // Admin không tham gia xếp hạng nên KHÔNG có bậc hạng nào, kể cả bậc kế tiếp: điểm của họ đã ngừng
        // tăng (xem AwardPointsAsync) nên hứa "còn N điểm nữa lên hạng" là hứa một mốc không bao giờ tới.
        // Trả về không hạng thì thanh tiến độ trên trang Tổng quan tự quay về đo tiến độ học của khóa học.
        if (await IsAdminAsync(userId))
            return new RankStatusDto(totalPoints, null, null, 0, 0m, []);

        var tierList = await _db.RankTier.AsNoTracking().OrderBy(x => x.MinPoints).ToListAsync();
        var currentTier = tierList.Where(x => x.MinPoints <= totalPoints).MaxBy(x => x.MinPoints);
        var nextTier = tierList.Where(x => x.MinPoints > totalPoints).MinBy(x => x.MinPoints);

        var pointsToNext = nextTier is null ? 0 : nextTier.MinPoints - totalPoints;
        var progressPercent = nextTier is null ? 100m
            : Math.Round((totalPoints - (currentTier?.MinPoints ?? 0)) * 100m
                / Math.Max(1, nextTier.MinPoints - (currentTier?.MinPoints ?? 0)), 1);

        var awardHistory = await _db.UserRankAward.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Include(x => x.RankTier)
            .Include(x => x.VoucherCode)
            .OrderByDescending(x => x.AwardedAt)
            .Select(x => new RankAwardDto(
                x.RankTierId, x.RankTier.Name, x.RankTier.Icon, x.RankTier.ColorHex,
                x.AwardedAt, x.RankTier.RewardDescription, x.VoucherCode != null ? x.VoucherCode.Code : null))
            .ToListAsync();

        return new RankStatusDto(
            totalPoints,
            currentTier is null ? null : ToBrief(currentTier),
            nextTier is null ? null : ToBrief(nextTier),
            pointsToNext, progressPercent, awardHistory);
    }

    /// <summary>Người này có phải tài khoản quản trị không — tính cả vai trò KẾ THỪA Admin, đúng bằng luật
    /// mà <see cref="RankableUserQuery"/> dùng để lọc bảng xếp hạng.</summary>
    private Task<bool> IsAdminAsync(int userId) => _db.User.AsNoTracking()
        .AnyAsync(x => x.Id == userId
                    && (x.Role.Name == RoleName.Admin
                     || (x.Role.ParentRole != null && x.Role.ParentRole.Name == RoleName.Admin)));

    /// <summary>Những người ĐƯỢC xếp hạng — dùng chung cho mọi bảng xếp hạng để hai bảng không bao giờ nói
    /// khác nhau về cùng một người.
    ///
    /// Loại Admin: đó là tài khoản quản trị hệ thống, không phải người đi học. Điểm họ có được là hệ quả của
    /// việc vào kiểm tra nội dung, đứng lẫn trong bảng vinh danh cạnh học viên thật thì bảng mất ý nghĩa.
    /// Loại luôn vai trò KẾ THỪA Admin ("Admin IT" nếu sau này có ai tạo): hệ thống ở mọi chỗ khác đều coi
    /// vai trò con mang danh tính của cha (xem Domain/Entities/Role.ParentRoleId), lọc mỗi tên vai trò là chừa ra một
    /// lối lách mà không ai để ý.
    ///
    /// Người đã nghỉ việc (IsActive = false) cũng không nên chiếm chỗ của người đang đi làm.</summary>
    private IQueryable<User> RankableUserQuery() => _db.User.AsNoTracking()
        .Where(x => x.IsActive
                 && x.Role.Name != RoleName.Admin
                 && (x.Role.ParentRole == null || x.Role.ParentRole.Name != RoleName.Admin));

    public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(int currentUserId, int top = 50,
        int? month = null, int? year = null, int? departmentId = null)
    {
        var tierList = await _db.RankTier.AsNoTracking().OrderBy(x => x.MinPoints).ToListAsync();

        var pointLogQuery = _db.UserPointLog.AsNoTracking().AsQueryable();
        if (month.HasValue && year.HasValue)
            pointLogQuery = pointLogQuery.Where(x => x.CreatedAt.Month == month && x.CreatedAt.Year == year);

        var statMap = await pointLogQuery
            .GroupBy(x => x.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Total = g.Sum(x => x.Points),
                LessonCount = g.Count(x => x.Reason == PointReason.LessonCompleted),
                QuizCount = g.Count(x => x.Reason == PointReason.QuizPassed),
                CourseCount = g.Count(x => x.Reason == PointReason.CourseCompleted),
            })
            .Where(x => x.Total > 0)
            .ToDictionaryAsync(x => x.UserId);

        if (statMap.Count == 0) return [];

        var userQuery = RankableUserQuery().Where(x => statMap.Keys.Contains(x.Id));
        if (departmentId.HasValue) userQuery = userQuery.Where(x => x.DepartmentId == departmentId);

        var userList = await userQuery
            .Select(x => new { x.Id, x.FullName, x.AvatarUrl, DepartmentName = x.Department != null ? x.Department.Name : null })
            .ToListAsync();

        return userList
            .Select(u =>
            {
                var stat = statMap[u.Id];
                var tier = tierList.Where(t => t.MinPoints <= stat.Total).MaxBy(t => t.MinPoints);
                return (u.Id, u.FullName, u.AvatarUrl, u.DepartmentName, Stat: stat, Tier: tier);
            })
            .OrderByDescending(x => x.Stat.Total)
            .Take(top)
            .Select((x, i) => new LeaderboardEntryDto(
                i + 1, x.Id, x.FullName, x.AvatarUrl, x.DepartmentName, x.Stat.Total,
                x.Tier?.Name, x.Tier?.Icon, x.Tier?.ColorHex, x.Id == currentUserId,
                x.Stat.LessonCount, x.Stat.QuizCount, x.Stat.CourseCount))
            .ToList();
    }

    /// <summary>Cửa sổ ngày dùng để dựng lại chuỗi học. Chuỗi dài hơn một năm là chuyện không xảy ra trong
    /// thực tế, mà giới hạn này giữ cho truy vấn không phải quét toàn bộ nhật ký điểm của mọi năm.</summary>
    private const int StreakWindowDay = 366;

    public async Task<List<StreakLeaderboardEntryDto>> GetStreakLeaderboardAsync(int currentUserId, int top = 20,
        bool byLongest = false)
    {
        var today = DateTime.Today;
        var fromDate = today.AddDays(-(StreakWindowDay - 1));

        // Lấy thẳng CreatedAt rồi mới cắt về ngày TRONG BỘ NHỚ, chứ không GroupBy theo x.CreatedAt.Date dưới
        // SQL: dự án chạy được trên cả Sqlite, SQL Server lẫn Oracle (xem Data/DbProviderSetup.cs) và phép
        // cắt ngày dịch ra ba câu SQL khác nhau, chỗ dịch được chỗ không. Đổi lại phải kéo về nhiều dòng hơn,
        // nhưng chỉ hai cột và bị chặn trong một năm nên vẫn nhẹ.
        var logList = await _db.UserPointLog.AsNoTracking()
            .Where(x => x.CreatedAt >= fromDate)
            .Select(x => new { x.UserId, x.CreatedAt })
            .ToListAsync();

        if (logList.Count == 0) return [];

        var dayListByUser = logList
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CreatedAt.Date).ToHashSet());

        var userList = await RankableUserQuery()
            .Where(x => dayListByUser.Keys.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.AvatarUrl, DepartmentName = x.Department != null ? x.Department.Name : null })
            .ToListAsync();

        return userList
            .Select(user =>
            {
                var daySet = dayListByUser[user.Id];
                return new
                {
                    user.Id,
                    user.FullName,
                    user.AvatarUrl,
                    user.DepartmentName,
                    Current = CountCurrentStreak(daySet, today),
                    Longest = CountLongestStreak(daySet),
                    ActiveDay = daySet.Count,
                    LastActive = daySet.Max(),
                };
            })
            // Chuỗi bằng 0 thì không phải "hạng thấp" mà là KHÔNG CÓ chuỗi nào — bày họ ra cuối bảng chỉ
            // làm loãng đúng thứ bảng này muốn nói.
            .Where(x => (byLongest ? x.Longest : x.Current) > 0)
            .OrderByDescending(x => byLongest ? x.Longest : x.Current)
            // Bằng nhau ở tiêu chí chính thì xét tiếp tiêu chí còn lại rồi tới tổng số ngày đã học. Không có
            // các mức phân định này thì thứ hạng đảo lộn sau mỗi lần tải trang, vì thứ tự giữa các bản ghi
            // bằng nhau là ngẫu nhiên.
            .ThenByDescending(x => byLongest ? x.Current : x.Longest)
            .ThenByDescending(x => x.ActiveDay)
            .ThenBy(x => x.FullName, StringComparer.CurrentCulture)
            .Take(top)
            .Select((x, index) => new StreakLeaderboardEntryDto(
                index + 1, x.Id, x.FullName, x.AvatarUrl, x.DepartmentName,
                x.Current, x.Longest, x.ActiveDay, x.LastActive, x.Id == currentUserId))
            .ToList();
    }

    /// <summary>Số ngày học liên tiếp tính ngược từ hôm nay.
    ///
    /// Giữ ĐÚNG quy ước của trang Tổng quan (xem CountStreakDay trong Controllers/DashboardService.cs):
    /// hôm nay chưa học thì chưa tính là đứt chuỗi mà đếm tiếp từ hôm qua, vì người học còn nguyên phần ngày
    /// còn lại để giữ chuỗi. Hai chỗ mà tính khác nhau thì cùng một người thấy hai con số chuỗi khác nhau
    /// trên hai màn hình, và không ai biết con số nào mới đúng.</summary>
    private static int CountCurrentStreak(HashSet<DateTime> daySet, DateTime today)
    {
        var cursor = daySet.Contains(today) ? today : today.AddDays(-1);

        var count = 0;
        while (daySet.Contains(cursor))
        {
            count++;
            cursor = cursor.AddDays(-1);
        }
        return count;
    }

    /// <summary>Chuỗi dài nhất từng đạt được trong cửa sổ đang xét — kỷ lục cá nhân, dùng để phân định thứ
    /// hạng khi hai người đang có cùng chuỗi hiện tại.</summary>
    private static int CountLongestStreak(HashSet<DateTime> daySet)
    {
        var longest = 0;
        foreach (var day in daySet)
        {
            // Chỉ đếm từ ngày MỞ ĐẦU một chuỗi (hôm trước không có hoạt động). Không có phép thử này thì mỗi
            // ngày trong chuỗi lại đếm lại từ đầu, biến phép tính thành O(n²) trên người học chăm nhất.
            if (daySet.Contains(day.AddDays(-1))) continue;

            var length = 0;
            var cursor = day;
            while (daySet.Contains(cursor))
            {
                length++;
                cursor = cursor.AddDays(1);
            }
            if (length > longest) longest = length;
        }
        return longest;
    }

    private static RankTierBriefDto ToBrief(RankTier tier) =>
        new(tier.Id, tier.Name, tier.Icon, tier.ColorHex, tier.MinPoints, tier.RewardDescription);
}
