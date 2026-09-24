// File: Application/Features/Courses/CourseAudience.cs

namespace HbELearning.Application.Features.Courses;

/// <summary>Quy tắc "khóa học này dành cho ai" nhìn từ phía học viên — dùng chung cho mục gợi ý
/// (<see cref="CourseService.GetMyAvailable"/>) và cho việc học viên tự đăng ký
/// (<see cref="EnrollmentService.SelfEnroll"/>). Hai nơi phải khớp tuyệt đối: gợi ý một khóa mà bấm đăng ký
/// lại bị từ chối, hoặc ngược lại đăng ký được khóa không hề được gợi ý, đều là lỗi.</summary>
public static class CourseAudience
{
    /// <summary>Khóa học đã xuất bản mà học viên ở phòng ban / vị trí này được phép tự đăng ký. Khóa không có
    /// dòng CourseTarget nào được coi là "dành cho tất cả".
    ///
    /// Chặn null ở hai vế so sánh: EF dịch "null == null" thành ĐÚNG, nên nếu không chặn, học viên chưa được gán
    /// phòng ban sẽ khớp mọi dòng đối tượng chỉ khai vị trí (DepartmentId = null) và thấy cả khóa không dành
    /// cho mình.</summary>
    public static IQueryable<Course> OpenFor(this IQueryable<Course> query, int? departmentId, int? positionId)
        => query.Where(x => x.Status == CourseStatus.Published
            && (!x.CourseTargetList.Any()
                || (departmentId != null && x.CourseTargetList.Any(t => t.DepartmentId == departmentId))
                || (positionId != null && x.CourseTargetList.Any(t => t.PositionId == positionId))));
}
