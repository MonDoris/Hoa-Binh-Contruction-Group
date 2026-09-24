// File: Domain/Entities/PointRule.cs

namespace HbELearning.Domain.Entities;

/// <summary>Số điểm cộng cho MỘT lần học viên làm xong một loại hoạt động (học xong bài, vượt bài kiểm tra,
/// hoàn thành khóa, chơi xong mô phỏng 3D).
///
/// Trước đây bốn con số này nằm cứng trong mã nguồn (RankingEngine.PointTable), nghĩa là muốn đổi "bài học
/// đáng mấy điểm" phải sửa mã và triển khai lại. Nhưng đây là thứ chính sách đào tạo, không phải thứ kỹ
/// thuật: kỳ này muốn đẩy mạnh mô phỏng thì nâng điểm mô phỏng lên, kỳ sau lại hạ xuống. Đưa vào bảng để
/// quản trị viên tự đặt.
///
/// Mỗi loại hoạt động chỉ có ĐÚNG một dòng (ràng buộc duy nhất trên Reason): hai dòng cùng loại thì điểm
/// cộng cho học viên phụ thuộc dòng nào được đọc trước — sai một cách ngẫu nhiên, rất khó lần ra.
///
/// Điểm ĐÃ CỘNG cho học viên không bị ảnh hưởng khi đổi bảng này: mỗi lần cộng đã ghi số điểm cụ thể vào
/// nhật ký (HBE_USER_POINT_LOG.POINTS). Đổi ở đây chỉ áp dụng cho những lần cộng SAU đó — nếu không, hạ
/// điểm một hoạt động sẽ làm tổng điểm của cả công ty tụt xuống và có người rơi khỏi bậc hạng đã đạt.</summary>
public class PointRule
{
    public int Id { get; set; }

    /// <summary>Loại hoạt động. Không dùng PointReason.WeeklyMissionCompleted — điểm thưởng nhiệm vụ tuần
    /// do quản trị viên đặt riêng cho từng nhiệm vụ (WeeklyMission.RewardPoint), không có mốc chung.</summary>
    public PointReason Reason { get; set; }

    /// <summary>Cho phép 0: có công ty muốn một hoạt động vẫn được ghi nhận trong nhật ký và đếm cho nhiệm
    /// vụ tuần nhưng không cộng điểm xếp hạng.</summary>
    public int Points { get; set; }

    /// <summary>Ngưỡng để hoạt động được tính, chỉ có nghĩa với loại hoạt động cần đo lường —
    /// hiện là <see cref="PointReason.DailyStudyTime"/>: học đủ bao nhiêu PHÚT trong ngày thì được cộng.
    ///
    /// null với những loại không cần ngưỡng (đăng nhập, trả lời đúng câu hỏi... — làm là xong, không có gì
    /// để đo). Để null thay vì 0 để phân biệt "loại này không dùng ngưỡng" với "ngưỡng bằng 0 nên lúc nào
    /// cũng đạt".</summary>
    public int? Threshold { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
