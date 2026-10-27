// File: Application/Features/Search/SearchService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Search;

/// <summary>Ô tìm kiếm trên thanh trên.
///
/// Phạm vi khóa học mà một người thấy được ở đây đi theo ĐÚNG quy tắc đối tượng của khóa học
/// (<see cref="CourseAudience.OpenFor"/>): khóa không khai đối tượng là của toàn công ty, khóa đã khai phòng ban /
/// vị trí thì chỉ người thuộc nhóm đó mới thấy. Dùng chung quy tắc với mục gợi ý và tự đăng ký là có chủ ý — tìm ra
/// một khóa mà bấm "Đăng ký học" lại bị từ chối, hay ngược lại, đều là lỗi. Ngoại lệ duy nhất: người soạn luôn
/// thấy khóa của chính mình.</summary>
public class SearchService : ISearchService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public SearchService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Search(string? keyword, CancellationToken cancellationToken)
    {
        var k = keyword?.Trim() ?? string.Empty;
        if (k.Length < MinKeywordLength) return Result.Success(new SearchResultDto([], []));
        if (k.Length > MaxKeywordLength) k = k[..MaxKeywordLength];

        var personList = await _db.User.AsNoTracking()
            .Where(x => x.IsActive
                && (x.FullName.Contains(k) || x.EmployeeCode.Contains(k) || (x.UserName != null && x.UserName.Contains(k))))
            // Tên bắt đầu đúng bằng từ khóa lên trước — gõ "Lan" thì "Lan Anh" đứng trên "Hoàng Thị Lan".
            .OrderBy(x => x.FullName.StartsWith(k) ? 0 : 1).ThenBy(x => x.FullName)
            .Take(MaxSuggestCount)
            .Select(x => new SearchPersonDto(
                x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl, x.Role.Name,
                x.Department != null ? x.Department.Name : null,
                x.Position != null ? x.Position.Name : null))
            .ToListAsync(cancellationToken);

        var courseList = await ToCourseDtoListAsync(
            VisibleCourseQuery().Where(x => x.Title.Contains(k) || x.Code.Contains(k))
                .OrderBy(x => x.Title.StartsWith(k) ? 0 : 1).ThenByDescending(x => x.CreatedAt)
                .Take(MaxSuggestCount),
            cancellationToken);

        return Result.Success(new SearchResultDto(personList, courseList));
    }

    public async Task<Result> GetPerson(int id, CancellationToken cancellationToken)
    {
        var person = await _db.User.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new
            {
                x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl, x.CoverUrl, x.Email, RoleName = x.Role.Name,
                DepartmentName = x.Department != null ? x.Department.Name : null,
                PositionName = x.Position != null ? x.Position.Name : null,
                x.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (person is null) return Result.NotFound(new MessageResponse("Không tìm thấy người này."));

        var certificateCount = await _db.Certificate.AsNoTracking()
            .CountAsync(x => x.Enrollment.UserId == id && !x.IsRevoked, cancellationToken);

        var courseList = await ToCourseDtoListAsync(
            VisibleCourseQuery().Where(x => x.CreatedById == id)
                .OrderByDescending(x => x.IsMandatory).ThenByDescending(x => x.CreatedAt)
                .Take(MaxAuthoredCourseCount),
            cancellationToken);

        return Result.Success(new SearchPersonDetailDto(
            person.Id, person.FullName, person.EmployeeCode, person.AvatarUrl, person.CoverUrl, person.Email,
            person.RoleName, person.DepartmentName, person.PositionName, person.CreatedAt,
            certificateCount, courseList));
    }

    /// <summary>Khóa học đã xuất bản mà người đang xem được thấy: dành cho mình theo quy tắc đối tượng
    /// (<see cref="CourseAudience.OpenFor"/>), hoặc do chính mình soạn. Bản nháp / chờ duyệt / lưu trữ không bao
    /// giờ hiện ở đây — kể cả với người soạn, vì từ ô tìm kiếm không có đường học một khóa chưa xuất bản.</summary>
    private IQueryable<Course> VisibleCourseQuery()
    {
        var viewerId = _currentUser.UserId;
        var openIdQuery = _db.Course.OpenFor(_currentUser.DepartmentId, _currentUser.PositionId).Select(x => x.Id);
        return _db.Course.AsNoTracking()
            .Where(x => x.Status == CourseStatus.Published
                && (x.CreatedById == viewerId || openIdQuery.Contains(x.Id)));
    }

    private async Task<List<SearchCourseDto>> ToCourseDtoListAsync(IQueryable<Course> query, CancellationToken cancellationToken)
    {
        var viewerId = _currentUser.UserId;
        var rowList = await query
            .Select(x => new
            {
                x.Id, x.Code, x.Title, x.ThumbnailUrl, x.DurationMinutes, x.IsMandatory,
                LessonCount = x.LessonList.Count(l => l.IsActive),
                IsTargeted = x.CourseTargetList.Any(),
                AuthorName = x.CreatedBy.FullName,
                x.CreatedById,
                // Lượt học của chính người xem. Lượt đã hủy vẫn trả về: giao diện cần biết để KHÔNG mời đăng ký lại
                // (tự đăng ký từ chối khóa đã từng bị gỡ phân công — xem EnrollmentService.SelfEnroll).
                Enrollment = x.EnrollmentList.Where(e => e.UserId == viewerId)
                    .Select(e => new { e.Id, e.Status }).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rowList.Select(x => new SearchCourseDto(
            x.Id, x.Code, x.Title, x.ThumbnailUrl, x.DurationMinutes, x.LessonCount, x.IsMandatory,
            x.IsTargeted ? "Targeted" : "Company", x.AuthorName,
            x.Enrollment?.Id, x.Enrollment?.Status.ToString(), x.CreatedById == viewerId)).ToList();
    }

    /// <summary>Gõ một chữ cái thì khớp gần như cả danh bạ — chờ tới chữ thứ hai mới tìm.</summary>
    private const int MinKeywordLength = 2;
    private const int MaxKeywordLength = 100;

    /// <summary>Số gợi ý mỗi nhóm trong ô xổ xuống — đủ để tìm, không dài tới mức phải cuộn.</summary>
    private const int MaxSuggestCount = 6;

    private const int MaxAuthoredCourseCount = 50;
}
