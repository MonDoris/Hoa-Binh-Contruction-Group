// File: Domain/Entities/UserDailyStudy.cs
namespace HbELearning.Domain.Entities;

/// <summary>Tổng thời gian một người học đã học trong MỘT ngày, cộng dồn từ nhịp heartbeat của trình học bài.
///
/// Vì sao phải có bảng riêng: hệ thống vốn chỉ cộng dồn thời gian theo TỪNG BÀI HỌC
/// (LessonProgress.TimeSpentSecond) — con số đó trả lời được "bài này học tổng cộng bao lâu" nhưng không trả
/// lời được "hôm nay học bao nhiêu phút", vì nó không mang ngày tháng. Mà "hôm nay học đủ 10 phút chưa" mới
/// là thứ cần cho hoạt động cộng điểm theo thói quen.
///
/// Chỉ đếm giây, KHÔNG lưu cờ "đã thưởng hay chưa": việc chống cộng trùng đã do ràng buộc duy nhất của
/// HBE_USER_POINT_LOG lo (mỗi (người, lý do, nguồn, ngày) đúng một dòng). Thêm một cờ nữa ở đây là tạo ra
/// hai nguồn sự thật cho cùng một câu hỏi, và chúng sẽ lệch nhau vào đúng ngày ai đó xóa tay một dòng điểm.</summary>
public class UserDailyStudy
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Ngày học, luôn cắt về 00:00 (DateTime.Today) để so sánh và gộp theo ngày không phụ thuộc giờ.</summary>
    public DateTime StudyDate { get; set; }

    public int StudySecond { get; set; }
}
