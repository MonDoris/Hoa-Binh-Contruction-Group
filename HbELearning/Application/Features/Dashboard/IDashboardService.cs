// File: Application/Features/Dashboard/IDashboardService.cs

namespace HbELearning.Application.Features.Dashboard;

/// <summary>Các ca sử dụng của chức năng Dashboard. Tầng API gọi qua cổng này (xem API/Controllers/DashboardService.cs).</summary>
public interface IDashboardService
{
    /// <summary>Chuỗi ngày học, điểm trong tuần và tiến độ các nhiệm vụ tuần của người đang đăng nhập.</summary>
    Task<Result> GetLearnerDashboard(CancellationToken cancellationToken);

    /// <summary>Toàn bộ nhiệm vụ tuần đang cấu hình, kể cả nhiệm vụ đang tắt.</summary>
    Task<Result> GetMissionList(CancellationToken cancellationToken);

    /// <summary>Các loại hoạt động HỌC có thể đặt nhiệm vụ, kèm điểm hệ thống cộng cho mỗi lần làm — để
    /// màn hình quản trị dựng ô xổ xuống, và để người đặt nhiệm vụ biết hoạt động đó vốn đáng bao nhiêu
    /// điểm trước khi quyết mức thưởng.
    ///
    /// Loại trừ WeeklyMissionCompleted: đó là dòng nhật ký của chính điểm thưởng nhiệm vụ. Cho phép đặt
    /// một nhiệm vụ đếm số nhiệm vụ đã xong là tạo ra vòng tự nuôi — thưởng lại kích hoạt thưởng.</summary>
    Task<Result> GetMissionActivityList();

    /// <summary>Bảng "điểm cho mỗi hoạt động" để quản trị viên xem và sửa.</summary>
    Task<Result> GetPointRuleList();

    /// <summary>Lưu lại bảng điểm.
    ///
    /// KHÔNG đụng tới điểm đã cộng cho học viên: mỗi lần cộng đã ghi số điểm cụ thể vào nhật ký
    /// (HBE_USER_POINT_LOG.POINTS), nên hạ điểm một hoạt động chỉ áp dụng cho những lần cộng SAU đó. Nếu
    /// tính lại quá khứ thì tổng điểm cả công ty sẽ tụt và có người rơi khỏi bậc hạng đã được trao —
    /// thu lại một phần thưởng đã trao là việc không hệ thống nào nên tự làm.</summary>
    Task<Result> SavePointRuleList(PointRuleSaveRequest request);

    Task<Result> CreateMission(WeeklyMissionRequest request);

    Task<Result> UpdateMission(int id, WeeklyMissionRequest request);

    /// <summary>Xóa hẳn một nhiệm vụ. Không cần kiểm gì trước như khi xóa bậc hạng: tiến độ được đếm lại
    /// từ nhật ký điểm mỗi lần mở trang, nên không người học nào đang "giữ" bản ghi này.
    ///
    /// Điểm thưởng đã cộng cho những người làm xong nhiệm vụ này thì Ở NGUYÊN — đó là điểm họ đã kiếm
    /// được, xóa đề bài không phải lý do để đòi lại. Muốn giữ lại đề bài thì tắt (IsActive = false).</summary>
    Task<Result> DeleteMission(int id);
}
