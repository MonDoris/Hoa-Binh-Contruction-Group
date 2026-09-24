// File: Domain/Common/WeekHelper.cs
namespace HbELearning.Domain.Common;

/// <summary>Mốc thời gian của "tuần" trong tính năng nhiệm vụ tuần.
///
/// Gom về một chỗ vì hai phía phải nói về CÙNG một tuần: màn hình Tổng quan đếm tiến độ từ mốc này, còn
/// RankingEngine dựa vào chính mốc đó để biết điểm thưởng của tuần này đã cộng chưa. Lệch nhau một ngày
/// là người học thấy nhiệm vụ đã xong mà không nhận được điểm, hoặc nhận hai lần trong một tuần.</summary>
public static class WeekHelper
{
    /// <summary>Thứ Hai của tuần chứa ngày đã cho. Tuần bắt đầu từ thứ Hai theo thói quen Việt Nam, không
    /// theo mặc định Chủ nhật của DayOfWeek.</summary>
    public static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;   // CN=0 -> 6, T2=1 -> 0, T3=2 -> 1...
        return date.AddDays(-offset).Date;
    }

    /// <summary>Giá trị SourceType của dòng nhật ký điểm thưởng nhiệm vụ, VD "WeeklyMission-20260831".
    ///
    /// Nhét ngày đầu tuần vào SourceType chứ không để trơn "WeeklyMission": ràng buộc duy nhất của bảng là
    /// (UserId, Reason, SourceType, SourceId), nên có mốc tuần trong đó thì mỗi người mỗi tuần chỉ nhận
    /// thưởng của một nhiệm vụ đúng một lần — mà tuần sau vẫn nhận lại được. Để trơn thì nhiệm vụ chỉ
    /// thưởng được đúng một lần trong đời.</summary>
    public static string MissionSourceType(DateTime weekStart) => $"WeeklyMission-{weekStart:yyyyMMdd}";
}
