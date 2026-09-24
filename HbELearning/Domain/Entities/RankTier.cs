// File: Domain/Entities/RankTier.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một bậc xếp hạng (VD Bronze, Silver, Gold) — đạt được khi tổng điểm tích lũy của người học >= MinPoints.</summary>
public class RankTier
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    /// <summary>Emoji hiển thị của bậc hạng (VD "🏆", "🥇") — lưu thẳng ký tự, không lưu markup. Bậc hạng
    /// tạo trước khi bộ chọn chuyển sang emoji còn giữ tên icon Phosphor cũ (VD "medal"); trang Xếp hạng quy
    /// đổi những tên đó sang emoji lúc hiển thị và ghi đè bằng emoji ở lần lưu tiếp theo, nên không cần
    /// migration. Giới hạn 40 ký tự thừa sức chứa cả emoji ghép nhiều mã (VD 🧑‍🏫 dài 5 ký tự).</summary>
    [Required, MaxLength(40)]
    public string Icon { get; set; } = null!;

    /// <summary>Màu chủ đạo hiển thị, dạng "#RRGGBB".</summary>
    [Required, MaxLength(20)]
    public string ColorHex { get; set; } = null!;

    public int MinPoints { get; set; }

    [MaxLength(300)]
    public string? RewardDescription { get; set; }

    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<RankVoucherCode> VoucherCodeList { get; set; } = new List<RankVoucherCode>();
    public ICollection<UserRankAward> AwardList { get; set; } = new List<UserRankAward>();
}
