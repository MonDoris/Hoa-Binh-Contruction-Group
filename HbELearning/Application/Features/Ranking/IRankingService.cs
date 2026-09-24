// File: Application/Features/Ranking/IRankingService.cs

namespace HbELearning.Application.Features.Ranking;

/// <summary>Các ca sử dụng của chức năng Ranking. Tầng API gọi qua cổng này (xem API/Controllers/RankingService.cs).</summary>
public interface IRankingService
{
    /// <summary>Trạng thái xếp hạng của người đang đăng nhập: điểm, bậc hiện tại/kế tiếp, lịch sử đạt hạng.</summary>
    Task<Result> GetMyStatus();

    /// <summary>
    /// Bảng xếp hạng theo tổng điểm, giảm dần. Truyền <c>month</c>+<c>year</c> để lọc theo tháng,
    /// <c>departmentId</c> để lọc theo phòng ban.
    /// </summary>
    Task<Result> GetLeaderboard(int top = 50, int? month = null, int? year = null, int? departmentId = null);

    /// <summary>Top người có chuỗi ngày học liên tiếp dài nhất. Công khai cho mọi vai trò như bảng xếp hạng
    /// toàn công ty — không lọc theo phòng ban nên không có gì để giới hạn phạm vi.</summary>
    /// <param name="mode">"longest" = xếp theo kỷ lục chuỗi dài nhất từng đạt. Mặc định: chuỗi đang giữ.</param>
    Task<Result> GetStreakLeaderboard(int top = 20, string? mode = null);

    /// <summary>
    /// Danh sách các bậc hạng đang cấu hình — ai cũng xem được để biết cần bao nhiêu điểm và phần thưởng gì.
    /// Nếu phòng ban của người gọi có phần thưởng riêng cho bậc hạng đó (do Giám Đốc phòng ban cấu hình),
    /// trả kèm trong DepartmentRewardDescription — phòng ban khác không thấy phần thưởng này.
    /// </summary>
    Task<Result> GetTierList();

    Task<Result> CreateTier(RankTierRequest request);

    Task<Result> UpdateTier(int id, RankTierRequest request);

    Task<Result> DeleteTier(int id);

    /// <summary>Thêm/sửa phần thưởng riêng của phòng ban mình cho 1 bậc hạng.</summary>
    Task<Result> SetDepartmentReward(int rankTierId, DepartmentRankRewardRequest request);

    /// <summary>Xóa phần thưởng riêng của phòng ban mình cho 1 bậc hạng (không ảnh hưởng phòng ban khác hay chính bậc hạng đó).</summary>
    Task<Result> DeleteDepartmentReward(int rankTierId);

    Task<Result> GetVoucherCodeList(int id);

    Task<Result> AddVoucherCode(int id, AddVoucherCodeRequest request);

    Task<Result> DeleteVoucherCode(int id);
}
