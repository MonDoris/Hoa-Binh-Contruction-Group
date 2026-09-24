// File: Application/Services/IRankingEngine.cs

namespace HbELearning.Application.Services;

public interface IRankingEngine
{
    /// <summary>Cộng điểm cho 1 hoạt động (idempotent — gọi lại nhiều lần cho cùng nguồn không cộng trùng), rồi tự kiểm tra và cấp bậc hạng mới nếu đủ điều kiện.</summary>
    Task AwardPointsAsync(int userId, PointReason reason, string sourceType, int sourceId);

    /// <summary>Cộng điểm cho một hoạt động chỉ tính MỘT LẦN MỖI NGÀY (đăng nhập, học đủ số phút quy định).
    ///
    /// Chống cộng trùng bằng chính ngày làm nguồn, nên gọi bao nhiêu lần trong ngày cũng chỉ cộng một lần —
    /// nhịp heartbeat gọi vào đây bốn lần mỗi phút mà không sợ gì.</summary>
    Task AwardDailyPointsAsync(int userId, PointReason reason, DateTime day);

    /// <summary>Ngưỡng quản trị viên đặt cho một hoạt động cần đo lường (VD số phút học tối thiểu trong
    /// ngày). Trả về null nếu loại hoạt động đó không dùng ngưỡng.</summary>
    Task<int?> GetThresholdAsync(PointReason reason);

    /// <summary>Trạng thái xếp hạng hiện tại của 1 người dùng: điểm, bậc hiện tại/kế tiếp, lịch sử đạt hạng.</summary>
    Task<RankStatusDto> GetStatusAsync(int userId);

    /// <summary>
    /// Bảng xếp hạng theo tổng điểm, giảm dần. Truyền cả <paramref name="month"/> và <paramref name="year"/>
    /// để chỉ tính điểm phát sinh trong tháng đó (mặc định: toàn thời gian). Truyền <paramref name="departmentId"/>
    /// để chỉ xếp hạng trong 1 phòng ban (mặc định: toàn công ty).
    /// </summary>
    Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(int currentUserId, int top = 50,
        int? month = null, int? year = null, int? departmentId = null);

    /// <summary>Bảng xếp hạng theo CHUỖI NGÀY HỌC LIÊN TIẾP, giảm dần.
    ///
    /// Khác hẳn bảng theo điểm chứ không phải một cách sắp xếp khác của cùng dữ liệu: điểm thưởng cho khối
    /// lượng (ai học nhiều bài hơn), còn chuỗi thưởng cho ĐỀU ĐẶN. Người mỗi ngày học một bài không bao giờ
    /// leo nổi bảng điểm cạnh người dồn hết vào hai hôm cuối quý, nhưng chính thói quen đều đặn đó mới là
    /// thứ đào tạo nội bộ muốn tạo ra — nên nó cần một bảng riêng để được nhìn thấy.</summary>
    /// <param name="byLongest">true = xếp theo KỶ LỤC (chuỗi dài nhất từng đạt) thay vì chuỗi đang giữ.
    /// Cần cả hai cách xếp vì bảng "đang giữ" trống trơn mỗi khi cả công ty vừa nghỉ một ngày — mà đó lại
    /// chính là lúc người ta vào xem, và một bảng trống thì chẳng nói lên điều gì.</param>
    Task<List<StreakLeaderboardEntryDto>> GetStreakLeaderboardAsync(int currentUserId, int top = 20, bool byLongest = false);
}
