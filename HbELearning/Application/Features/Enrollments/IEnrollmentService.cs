// File: Application/Features/Enrollments/IEnrollmentService.cs

namespace HbELearning.Application.Features.Enrollments;

/// <summary>Các ca sử dụng của chức năng Enrollment. Tầng API gọi qua cổng này (xem API/Controllers/EnrollmentService.cs).</summary>
public interface IEnrollmentService
{
    /// <summary>Phân công khóa học cho danh sách người học cụ thể.</summary>
    Task<Result> Assign(AssignRequest request);

    /// <summary>Phân công hàng loạt theo phòng ban / vị trí công việc.</summary>
    Task<Result> AssignByTarget(AssignByTargetRequest request);

    /// <summary>Học viên tự đăng ký một khóa học trong mục gợi ý "Khóa học phù hợp với bạn". Chỉ nhận khóa đã
    /// xuất bản và dành cho phòng ban / vị trí của chính người đó (xem <see cref="CourseAudience.OpenFor"/>).</summary>
    Task<Result> SelfEnroll(int courseId);

    /// <summary>Khóa học của tôi.</summary>
    Task<Result> GetMy(EnrollmentStatus? status);

    /// <summary>Các bài học của tôi đã ghim lên lịch — mỗi bài rơi vào NGÀY MỞ của nó trong lộ trình
    /// (PathItem.StartDate). Bài nào người phụ trách không gán lịch thì mới rơi về ngày tạo (Lesson.CreatedAt),
    /// vì lúc đó bài mở ngay theo thứ tự tuần tự chứ không có mốc ngày nào khác để ghim.
    ///
    /// Chỉ nhận khoảng ngày (from/to) chứ không trả cả đời: lịch chỉ hiện một tháng nên kéo hết bài học của
    /// mọi khóa về rồi lọc ở trình duyệt là phí. Bỏ trống from/to thì trả tất cả (dùng cho màn hình khác).</summary>
    Task<Result> GetMySchedule(DateTime? from, DateTime? to);

    /// <summary>Danh sách người học của một khóa học — dành cho Trainer/Admin và các cấp duyệt (Trưởng phòng/Giám Đốc) xem trong lúc duyệt nội dung.</summary>
    Task<Result> GetByCourse(int courseId, EnrollmentStatus? status, int? departmentId);

    /// <summary>Toàn bộ khóa học đã phân công cho một người học cụ thể (dùng cho báo cáo đào tạo theo từng nhân viên).
    /// Giám Đốc chỉ xem được người học trong phòng ban của mình.</summary>
    Task<Result> GetByUser(int userId);

    /// <summary>Chi tiết một bản ghi phân công kèm trạng thái từng bước trong lộ trình.</summary>
    Task<Result> GetById(int id);

    Task<Result> Cancel(int id);
}
