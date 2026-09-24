// File: Application/DTOs/DashboardDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Số liệu gamification của trang Tổng quan cho người học.
///
/// Mọi con số ở đây đều tính từ hoạt động THẬT trong HBE_USER_POINT_LOG — mỗi dòng nhật ký là một lần hoàn
/// thành bài học / vượt bài kiểm tra / xong khóa học / xong mô phỏng. Không có bảng riêng nào cho "chuỗi
/// ngày" hay "nhiệm vụ": chúng là các cách ĐỌC khác nhau trên cùng nhật ký đó, nên không bao giờ lệch với
/// điểm xếp hạng và cũng không cần công việc nền nào để đồng bộ.</summary>
/// <param name="StreakDay">Số ngày liên tiếp có học, tính ngược từ hôm nay. Hôm nay chưa học thì vẫn tính
/// tiếp chuỗi tới hôm qua — người học còn nguyên buổi tối để giữ chuỗi, báo đứt ngay từ sáng là sai.</param>
/// <param name="WeekPoint">Điểm kiếm được trong tuần này (từ thứ Hai).</param>
/// <param name="TotalPoint">Tổng điểm tích lũy — cũng là con số dùng cho cấp bậc.</param>
public record LearnerDashboardDto(
    int StreakDay,
    int WeekPoint,
    int TotalPoint,
    IReadOnlyList<StreakDayDto> DayList,
    IReadOnlyList<WeeklyMissionDto> MissionList);

/// <summary>Một ngày trong dải chuỗi học.</summary>
/// <param name="WeekdayLabel">Nhãn ngắn hiển thị dưới ô: T2..CN.</param>
public record StreakDayDto(DateTime Date, string WeekdayLabel, bool HasActivity, bool IsToday);

/// <summary>Một nhiệm vụ của tuần này, tính từ nhật ký điểm.</summary>
/// <param name="Point">Điểm thưởng khi làm ĐỦ chỉ tiêu — cộng một lần cho cả nhiệm vụ, do quản trị viên
/// đặt. Không phải điểm mỗi lần làm: điểm của từng hoạt động lẻ vẫn cộng riêng như thường. Đây là con số
/// người học thực nhận, RankingEngine cộng đúng bằng nó chứ không phải một mức thưởng trên giấy.</param>
public record WeeklyMissionDto(string Key, string Label, int Current, int Target, int Point)
{
    public bool IsDone => Current >= Target;
}

// ==================== QUẢN TRỊ NHIỆM VỤ TUẦN ====================

/// <summary>Một nhiệm vụ tuần dưới góc nhìn quản trị viên — kèm cả nhiệm vụ đang tắt, thứ mà người học
/// không bao giờ thấy.</summary>
/// <param name="Activity">Tên loại hoạt động được đếm (VD "LessonCompleted"), khớp <see cref="PointReason"/>.</param>
/// <param name="RewardPoint">Điểm thưởng khi làm đủ chỉ tiêu, do quản trị viên đặt.</param>
public record WeeklyMissionDetailDto(
    int Id, string Label, string Activity, int Target, int RewardPoint, int OrderIndex, bool IsActive);

/// <summary>Một loại hoạt động có thể đặt nhiệm vụ, dùng để dựng ô xổ xuống trong màn hình quản trị.</summary>
/// <param name="Value">Giá trị số của <see cref="PointReason"/> — đúng thứ cần gửi lại khi lưu.</param>
/// <param name="Point">Điểm hệ thống cộng cho mỗi lần làm hoạt động này. Chỉ để tham khảo khi cân mức
/// thưởng — nó vẫn được cộng như thường, độc lập với nhiệm vụ.</param>
public record MissionActivityDto(int Value, string Name, int Point);

/// <summary>Một dòng trong bảng "điểm cho mỗi hoạt động" mà quản trị viên tự đặt.
/// <paramref name="Threshold"/> chỉ khác null với hoạt động cần đo lường (hiện là "học đủ N phút trong
/// ngày") — giao diện dựa vào đó để biết có phải vẽ thêm ô nhập ngưỡng hay không.</summary>
public record PointRuleDto(int Value, string Name, int Point, int? Threshold);

/// <summary>Cả bảng điểm gửi lên trong MỘT lượt lưu, không lưu lẻ từng dòng: bốn con số này là một chính
/// sách, sửa xong mới bấm lưu. Lưu lẻ thì mạng rớt giữa chừng sẽ để lại nửa bảng cũ nửa bảng mới.</summary>
public class PointRuleSaveRequest
{
    public List<PointRuleItemRequest> ItemList { get; set; } = new();
}

public class PointRuleItemRequest
{
    /// <summary>Giá trị số của PointReason.</summary>
    public int Reason { get; set; }

    /// <summary>0 = ghi nhận hoạt động nhưng không cộng điểm xếp hạng. Chặn trên để một cú gõ nhầm không
    /// đẩy một người lên bậc cao nhất ngay lập tức.</summary>
    [System.ComponentModel.DataAnnotations.Range(0, 10000)]
    public int Point { get; set; }

    /// <summary>Ngưỡng của hoạt động cần đo lường (số phút với "học đủ N phút trong ngày"). Bỏ trống với
    /// những loại không dùng ngưỡng. Chặn trên 1440 = một ngày: đặt hơn thế thì không ai đạt nổi.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 1440)]
    public int? Threshold { get; set; }
}

public class WeeklyMissionRequest
{
    [Required(ErrorMessage = "Tên nhiệm vụ là bắt buộc"), MaxLength(200)]
    public string Label { get; set; } = null!;

    /// <summary>Loại hoạt động được đếm. Nhận giá trị số của <see cref="PointReason"/>, giống cách các màn
    /// hình khác gửi enum lên (VD ContentType của bài học).</summary>
    public PointReason Reason { get; set; }

    /// <summary>Trần 999 chỉ để chặn con số vô nghĩa gõ nhầm (mục tiêu 100.000 bài/tuần) — không phải giới
    /// hạn nghiệp vụ nào.</summary>
    [Range(1, 999, ErrorMessage = "Chỉ tiêu phải từ 1 trở lên")]
    public int Target { get; set; } = 1;

    /// <summary>Điểm thưởng khi làm đủ chỉ tiêu. 0 = nhiệm vụ không kèm thưởng, chỉ để theo dõi.</summary>
    [Range(0, 9999, ErrorMessage = "Điểm thưởng phải từ 0 đến 9999")]
    public int RewardPoint { get; set; }

    public int OrderIndex { get; set; }

    public bool IsActive { get; set; } = true;
}
