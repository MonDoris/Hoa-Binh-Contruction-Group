// File: Application/DTOs/SearchDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Kết quả gợi ý của ô tìm kiếm trên thanh trên: vài người và vài khóa học khớp từ khóa.</summary>
public record SearchResultDto(List<SearchPersonDto> PersonList, List<SearchCourseDto> CourseList);

/// <summary>Một người trong danh bạ công ty. Cố ý KHÔNG có số điện thoại hay thông tin đăng nhập — đây là thứ mọi
/// nhân viên đều tra được về nhau.</summary>
public record SearchPersonDto(
    int Id, string FullName, string EmployeeCode, string? AvatarUrl,
    string RoleName, string? DepartmentName, string? PositionName);

/// <summary>Một khóa học người tìm được phép thấy (xem SearchService.VisibleCourseQuery).
///
/// <paramref name="Audience"/>: "Company" = dành cho toàn công ty, "Targeted" = chỉ dành cho một số phòng ban /
/// vị trí (người xem thấy được vì mình thuộc nhóm đó, hoặc vì chính mình soạn).
/// <paramref name="EnrollmentId"/> / <paramref name="EnrollmentStatus"/>: lượt học của CHÍNH người xem, null nếu
/// chưa đăng ký — giao diện dựa vào đây để hiện nút "Vào học" hay "Đăng ký học".</summary>
public record SearchCourseDto(
    int Id, string Code, string Title, string? ThumbnailUrl, int DurationMinutes, int LessonCount, bool IsMandatory,
    string Audience, string? AuthorName, int? EnrollmentId, string? EnrollmentStatus,
    /// <summary>Người xem chính là người soạn — giao diện mời "Mở khóa học" (trang soạn) thay vì "Đăng ký học".</summary>
    bool IsMine);

/// <summary>Hồ sơ một người khi bấm vào kết quả tìm kiếm: thông tin trong hệ thống + các khóa học họ soạn mà
/// người xem được phép thấy.</summary>
public record SearchPersonDetailDto(
    int Id, string FullName, string EmployeeCode, string? AvatarUrl, string? CoverUrl, string Email,
    string RoleName, string? DepartmentName, string? PositionName, DateTime CreatedAt,
    int CertificateCount, List<SearchCourseDto> AuthoredCourseList);
