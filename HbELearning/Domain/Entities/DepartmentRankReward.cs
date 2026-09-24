// File: Domain/Entities/DepartmentRankReward.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Phần thưởng bổ sung riêng của 1 phòng ban cho 1 bậc hạng, do Giám Đốc phòng ban đó cấu hình.
/// Không thay thế RewardDescription chung của RankTier (do Admin quản lý) — chỉ hiển thị thêm cho
/// nhân viên cùng phòng ban với Giám Đốc đã đặt phần thưởng, phòng ban khác không thấy.
/// </summary>
public class DepartmentRankReward
{
    public int Id { get; set; }

    public int RankTierId { get; set; }
    public RankTier RankTier { get; set; } = null!;

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    [Required, MaxLength(300)]
    public string RewardDescription { get; set; } = null!;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
