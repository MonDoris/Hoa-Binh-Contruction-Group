// File: Domain/Entities/RankVoucherCode.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một mã voucher trong kho mã của 1 bậc hạng, do quản trị viên nhập tay — cấp tự động cho người học đầu tiên đạt bậc này khi còn mã trống.</summary>
public class RankVoucherCode
{
    public int Id { get; set; }

    public int RankTierId { get; set; }
    public RankTier RankTier { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Code { get; set; } = null!;

    public bool IsUsed { get; set; }

    public int? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }
    public DateTime? AssignedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
