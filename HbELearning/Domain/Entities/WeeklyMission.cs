// File: Domain/Entities/WeeklyMission.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một nhiệm vụ tuần hiện trong ô "Nhiệm vụ tuần này" trên trang Tổng quan của người học.
///
/// Nhiệm vụ KHÔNG có bảng tiến độ riêng: tiến độ mỗi tuần được đếm lại từ HBE_USER_POINT_LOG (xem
/// DashboardService). Bảng này chỉ lưu ĐỀ BÀI — đếm loại hoạt động nào, bao nhiêu lần thì xong — nên
/// quản trị viên sửa chỉ tiêu lúc nào cũng được mà không phải tính lại tiến độ của ai.</summary>
public class WeeklyMission
{
    public int Id { get; set; }

    /// <summary>Câu chữ hiện cho người học, VD "Học xong 5 bài học". Cố ý để quản trị viên tự viết thay vì
    /// ghép máy móc từ Target: nhiệm vụ nào cũng cùng một khuôn thì ô nhiệm vụ đọc như bảng kê, không ai để ý.</summary>
    [Required, MaxLength(200)]
    public string Label { get; set; } = null!;

    /// <summary>Loại hoạt động được đếm. Dùng chung enum với nhật ký điểm để tiến độ nhiệm vụ và điểm xếp
    /// hạng luôn nói về cùng một việc — không có loại hoạt động nào chỉ nhiệm vụ mới biết.</summary>
    public PointReason Reason { get; set; }

    /// <summary>Số lần phải làm trong tuần thì nhiệm vụ coi như xong.</summary>
    public int Target { get; set; } = 1;

    /// <summary>Điểm thưởng cộng thêm khi làm đủ chỉ tiêu trong tuần, do quản trị viên tự đặt.
    ///
    /// Là điểm của CẢ nhiệm vụ, cộng MỘT lần mỗi tuần — không phải điểm mỗi lần làm. Người học vẫn nhận
    /// bình thường điểm của từng hoạt động lẻ theo bảng điểm hệ thống (xem RankingEngine.PointTable);
    /// khoản này chồng lên trên. Để 0 nếu muốn một nhiệm vụ chỉ để theo dõi, không kèm thưởng.</summary>
    public int RewardPoint { get; set; }

    /// <summary>Thứ tự hiện trên ô nhiệm vụ, số nhỏ lên trước.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Tạm ẩn khỏi trang Tổng quan mà vẫn giữ lại đề bài. Có cờ này thì việc bỏ một nhiệm vụ theo
    /// mùa vụ không cần xóa rồi gõ lại y nguyên vào tuần sau.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
