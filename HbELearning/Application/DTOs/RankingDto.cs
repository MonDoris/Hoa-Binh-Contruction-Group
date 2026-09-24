// File: Application/DTOs/RankingDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record RankTierDto(
    int Id, string Name, string Icon, string ColorHex, int MinPoints,
    string? RewardDescription, int OrderIndex, int AvailableCodeCount, int UsedCodeCount,
    string? DepartmentRewardDescription = null);

public record RankTierBriefDto(int Id, string Name, string Icon, string ColorHex, int MinPoints, string? RewardDescription);

public class RankTierRequest
{
    [Required(ErrorMessage = "Tên bậc hạng là bắt buộc"), MaxLength(50)]
    public string Name { get; set; } = null!;

    /// <summary>Emoji hiển thị của bậc hạng, lưu thẳng ký tự — xem <see cref="RankTier.Icon"/>.</summary>
    [Required(ErrorMessage = "Biểu tượng là bắt buộc"), MaxLength(40)]
    public string Icon { get; set; } = null!;

    [Required(ErrorMessage = "Màu sắc là bắt buộc"), MaxLength(20)]
    public string ColorHex { get; set; } = null!;

    [Range(0, int.MaxValue, ErrorMessage = "Điểm tối thiểu phải >= 0")]
    public int MinPoints { get; set; }

    [MaxLength(300)]
    public string? RewardDescription { get; set; }

    public int OrderIndex { get; set; }
}

/// <summary>Nội dung phần thưởng riêng của phòng ban cho 1 bậc hạng, do Giám Đốc phòng ban đó cấu hình.</summary>
public class DepartmentRankRewardRequest
{
    [Required(ErrorMessage = "Nội dung phần thưởng là bắt buộc"), MaxLength(300)]
    public string RewardDescription { get; set; } = null!;
}

public record RankVoucherCodeDto(int Id, string Code, bool IsUsed, string? AssignedToUserName, DateTime? AssignedAt);

public class AddVoucherCodeRequest
{
    /// <summary>Nhiều mã, mỗi dòng 1 mã.</summary>
    [Required(ErrorMessage = "Vui lòng nhập ít nhất 1 mã")]
    public string Codes { get; set; } = null!;
}

public record RankAwardDto(int RankTierId, string TierName, string Icon, string ColorHex, DateTime AwardedAt, string? RewardDescription, string? VoucherCode);

public record RankStatusDto(
    int TotalPoints, RankTierBriefDto? CurrentTier, RankTierBriefDto? NextTier,
    int PointsToNext, decimal ProgressPercent, List<RankAwardDto> AwardHistory);

/// <summary>Một dòng trên bảng xếp hạng chuỗi ngày học liên tiếp.</summary>
/// <param name="CurrentStreakDay">Chuỗi ĐANG giữ, tính ngược từ hôm nay — con số quyết định thứ hạng.</param>
/// <param name="LongestStreakDay">Chuỗi dài nhất từng đạt trong một năm gần đây (kỷ lục cá nhân).</param>
/// <param name="ActiveDayCount">Tổng số ngày có học trong một năm gần đây — cho thấy chuỗi hiện tại là thói
/// quen lâu dài hay chỉ mới bắt đầu được vài hôm.</param>
public record StreakLeaderboardEntryDto(
    int Rank, int UserId, string FullName, string? AvatarUrl, string? DepartmentName,
    int CurrentStreakDay, int LongestStreakDay, int ActiveDayCount, DateTime LastActiveDate, bool IsMe);

public record LeaderboardEntryDto(
    int Rank, int UserId, string FullName, string? AvatarUrl, string? DepartmentName,
    int TotalPoints, string? TierName, string? TierIcon, string? TierColorHex, bool IsMe,
    int LessonCompletedCount, int QuizPassedCount, int CourseCompletedCount);
