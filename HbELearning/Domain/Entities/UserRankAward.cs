// File: Domain/Entities/UserRankAward.cs
namespace HbELearning.Domain.Entities;

/// <summary>Ghi nhận một lần người dùng đạt tới 1 bậc hạng — mỗi (UserId, RankTierId) chỉ có tối đa 1 bản ghi.</summary>
public class UserRankAward
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int RankTierId { get; set; }
    public RankTier RankTier { get; set; } = null!;

    public DateTime AwardedAt { get; set; } = DateTime.Now;

    /// <summary>Mã voucher đã cấp cho lần đạt hạng này — null nếu kho mã của bậc này đang hết lúc đạt hạng.</summary>
    public int? VoucherCodeId { get; set; }
    public RankVoucherCode? VoucherCode { get; set; }
}
