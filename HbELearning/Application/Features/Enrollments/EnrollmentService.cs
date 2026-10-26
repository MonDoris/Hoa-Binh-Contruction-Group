// File: Application/Features/Enrollments/EnrollmentService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Enrollments;

public class EnrollmentService : IEnrollmentService
{
    private readonly IAppDbContext _db;
    private readonly ILearningService _learningService;
    private readonly INotificationDispatcher _notificationService;
    private readonly ICurrentUser _currentUser;

    public EnrollmentService(IAppDbContext db, ILearningService learningService, INotificationDispatcher notificationService, ICurrentUser currentUser)
    {
        _db = db;
        _learningService = learningService;
        _notificationService = notificationService;
        _currentUser = currentUser;
    }

    public async Task<Result> Assign(AssignRequest request)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == request.CourseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (course.Status != CourseStatus.Published)
            return Result.Invalid(new MessageResponse("Chỉ có thể phân công khóa học đã xuất bản."));
        if (request.DueDate.HasValue && request.DueDate.Value.Date < DateTime.Now.Date)
            return Result.Invalid(new MessageResponse("Hạn hoàn thành không được ở trong quá khứ."));

        var existingUserIdList = await _db.Enrollment
            .Where(x => x.CourseId == request.CourseId && request.UserIdList.Contains(x.UserId))
            .Select(x => x.UserId).ToListAsync();

        var validUserIdList = await _db.User
            .Where(x => request.UserIdList.Contains(x.Id) && x.IsActive && !existingUserIdList.Contains(x.Id))
            .Select(x => x.Id).ToListAsync();

        var dueDate = request.DueDate
            ?? (course.DeadlineDay.HasValue ? DateTime.Now.AddDays(course.DeadlineDay.Value) : null);

        var newEnrollmentList = new List<Enrollment>();
        foreach (var userId in validUserIdList)
        {
            var enrollment = new Enrollment
            {
                UserId = userId,
                CourseId = request.CourseId,
                AssignedById = _currentUser.UserId,
                AssignedAt = DateTime.Now,
                DueDate = dueDate,
                Status = EnrollmentStatus.Assigned
            };
            newEnrollmentList.Add(enrollment);
            _db.Enrollment.Add(enrollment);
        }

        await _db.SaveChangesAsync();

        foreach (var enrollment in newEnrollmentList)
            await _notificationService.NotifyCourseAssignedAsync(enrollment.Id);

        return Result.Success(new MessageResponse(
            $"Đã phân công cho {validUserIdList.Count} người học. Bỏ qua {existingUserIdList.Count} người đã được phân công trước đó."));
    }

    public async Task<Result> AssignByTarget(AssignByTargetRequest request)
    {
        if (request.DepartmentId is null && request.PositionId is null)
            return Result.Invalid(new MessageResponse("Phải chọn phòng ban hoặc vị trí công việc."));

        var query = _db.User.Where(x => x.IsActive);
        if (request.DepartmentId.HasValue) query = query.Where(x => x.DepartmentId == request.DepartmentId);
        if (request.PositionId.HasValue) query = query.Where(x => x.PositionId == request.PositionId);

        var userIdList = await query.Select(x => x.Id).ToListAsync();
        if (userIdList.Count == 0)
            return Result.Invalid(new MessageResponse("Không tìm thấy người học phù hợp."));

        return await Assign(new AssignRequest
        {
            CourseId = request.CourseId,
            UserIdList = userIdList,
            DueDate = request.DueDate
        });
    }

    public async Task<Result> SelfEnroll(int courseId)
    {
        // Cùng một quy tắc với mục gợi ý (CourseAudience.OpenFor): khóa chưa xuất bản hoặc không dành cho phòng
        // ban / vị trí của mình thì coi như không tồn tại, không lộ ra là có khóa đó.
        var course = await _db.Course
            .OpenFor(_currentUser.DepartmentId, _currentUser.PositionId)
            .FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null)
            return Result.NotFound(new MessageResponse("Khóa học không tồn tại hoặc không mở cho bạn tự đăng ký."));

        // Kể cả bản ghi đã bị hủy: người phụ trách đã chủ động gỡ thì học viên không tự đăng ký lại được.
        if (await _db.Enrollment.AnyAsync(x => x.CourseId == courseId && x.UserId == _currentUser.UserId))
            return Result.Invalid(new MessageResponse("Bạn đã có khóa học này trong danh sách."));

        var enrollment = new Enrollment
        {
            UserId = _currentUser.UserId,
            CourseId = courseId,
            AssignedById = _currentUser.UserId,
            AssignedAt = DateTime.Now,
            DueDate = course.DeadlineDay.HasValue ? DateTime.Now.AddDays(course.DeadlineDay.Value) : null,
            Status = EnrollmentStatus.Assigned
        };
        _db.Enrollment.Add(enrollment);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Bấm đăng ký hai lần gần như cùng lúc: lần sau vấp chỉ mục duy nhất (UserId, CourseId).
            return Result.Invalid(new MessageResponse("Bạn đã có khóa học này trong danh sách."));
        }

        // Không gửi thông báo "được phân công": chính học viên vừa bấm đăng ký.
        return Result.Success(new MessageResponse($"Đã đăng ký khóa học \"{course.Title}\"."));
    }

    public async Task<Result> GetMy(EnrollmentStatus? status)
    {
        var query = _db.Enrollment.AsNoTracking().Where(x => x.UserId == _currentUser.UserId);
        if (status.HasValue) query = query.Where(x => x.Status == status);

        return Result.Success(await ProjectAsync(query.OrderByDescending(x => x.AssignedAt)));
    }

    public async Task<Result> GetMySchedule(DateTime? from, DateTime? to)
    {
        var query =
            from e in _db.Enrollment.AsNoTracking()
            where e.UserId == _currentUser.UserId && e.Status != EnrollmentStatus.Cancelled
            join l in _db.Lesson.AsNoTracking() on e.CourseId equals l.CourseId
            where l.IsActive
            select new
            {
                EnrollmentId = e.Id,
                e.CourseId,
                CourseTitle = e.Course.Title,
                LessonId = l.Id,
                LessonTitle = l.Title,
                l.ContentType,
                l.DurationMinutes,
                l.CreatedAt,
                // Ngày mở bài trong lộ trình của khóa. Một bài chỉ nằm ở một mắt xích nên FirstOrDefault là đủ;
                // null = mắt xích không gán lịch (hoặc khóa chưa dựng lộ trình) -> quy về CreatedAt bên dưới.
                StartDate = _db.PathItem
                    .Where(pi => pi.LessonId == l.Id
                              && pi.LearningPath.CourseId == e.CourseId
                              && pi.StartDate != null)
                    .OrderBy(pi => pi.OrderIndex)
                    .Select(pi => pi.StartDate)
                    .FirstOrDefault(),
                // Bài chưa từng mở thì không có bản ghi LessonProgress — để null rồi quy về NotStarted bên dưới.
                Status = _db.LessonProgress
                    .Where(p => p.EnrollmentId == e.Id && p.LessonId == l.Id)
                    .Select(p => (ProgressStatus?)p.Status)
                    .FirstOrDefault()
            };

        // Lọc theo đúng ngày sẽ hiện trên lịch (ngày mở, thiếu thì ngày tạo) — lọc theo CreatedAt sẽ đánh rơi
        // bài tạo tháng trước nhưng hẹn mở tháng này.
        // Tính mốc ra biến trước khi đưa vào biểu thức: EF chỉ cần tham số hằng, không phải dịch .Date/.AddDays.
        if (from.HasValue)
        {
            var fromDate = from.Value.Date;
            query = query.Where(x => (x.StartDate ?? x.CreatedAt) >= fromDate);
        }
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            query = query.Where(x => (x.StartDate ?? x.CreatedAt) < toExclusive);
        }

        var raw = await query.ToListAsync();

        return Result.Success(raw
            .Select(x => new ScheduleItemDto(
                x.EnrollmentId, x.CourseId, x.CourseTitle,
                x.LessonId, x.LessonTitle, x.ContentType.ToString(),
                x.DurationMinutes, x.StartDate ?? x.CreatedAt,
                (x.Status ?? ProgressStatus.NotStarted).ToString()))
            .OrderBy(x => x.ScheduledAt).ThenBy(x => x.LessonTitle)
            .ToList());
    }

    public async Task<Result> GetByCourse(int courseId, EnrollmentStatus? status, int? departmentId)
    {
        var query = _db.Enrollment.AsNoTracking().Where(x => x.CourseId == courseId);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (departmentId.HasValue) query = query.Where(x => x.User.DepartmentId == departmentId);

        return Result.Success(await ProjectAsync(query.OrderBy(x => x.User.EmployeeCode)));
    }

    public async Task<Result> GetByUser(int userId)
    {
        var user = await _db.User.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người học."));
        if (_currentUser.IsDirector && user.DepartmentId != _currentUser.DepartmentId) return Result.Forbidden();

        var query = _db.Enrollment.AsNoTracking().Where(x => x.UserId == userId);
        return Result.Success(await ProjectAsync(query.OrderByDescending(x => x.AssignedAt)));
    }

    public async Task<Result> GetById(int id)
    {
        var enrollment = await _db.Enrollment.AsNoTracking()
            .Include(x => x.Course)
            .Include(x => x.User)
            .Include(x => x.Certificate)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (!_currentUser.IsManager && enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var path = await _db.LearningPath.AsNoTracking().FirstOrDefaultAsync(x => x.CourseId == enrollment.CourseId);
        var pathList = await _learningService.BuildPathProgressAsync(id);

        return Result.Success(new EnrollmentDetailDto(
            enrollment.Id, enrollment.CourseId, enrollment.Course.Code, enrollment.Course.Title,
            enrollment.Course.Description, enrollment.Course.PassScore, path?.IsSequential ?? true,
            enrollment.UserId, enrollment.User.FullName,
            enrollment.Status.ToString(), enrollment.ProgressPercent, enrollment.FinalScore,
            enrollment.AssignedAt, enrollment.DueDate, enrollment.StartedAt, enrollment.CompletedAt,
            enrollment.Certificate is not null, enrollment.Certificate?.Id,
            pathList));
    }

    public async Task<Result> Cancel(int id)
    {
        var enrollment = await _db.Enrollment.FirstOrDefaultAsync(x => x.Id == id);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));

        if (!enrollment.CanBeCancelled)
            return Result.Invalid(new MessageResponse("Không thể hủy khóa học đã hoàn thành."));

        enrollment.Cancel();
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã hủy phân công."));
    }

    // ---------- Helper ----------
    private async Task<List<EnrollmentListDto>> ProjectAsync(IQueryable<Enrollment> query)
    {
        var now = DateTime.Now;

        var raw = await query.Select(x => new
        {
            x.Id, x.CourseId, x.Course.Code, x.Course.Title, x.Course.ThumbnailUrl,
            x.UserId, UserFullName = x.User.FullName, x.User.EmployeeCode,
            DepartmentName = x.User.Department != null ? x.User.Department.Name : null,
            x.Status, x.ProgressPercent, x.FinalScore,
            x.AssignedAt, x.DueDate, x.StartedAt, x.CompletedAt,
            HasCertificate = x.Certificate != null,
            ReviewCount = _db.CourseFeedback.Count(f => f.CourseId == x.CourseId && f.Kind == FeedbackKind.Review && f.Rating != null),
            AverageRating = _db.CourseFeedback
                .Where(f => f.CourseId == x.CourseId && f.Kind == FeedbackKind.Review && f.Rating != null)
                .Average(f => (double?)f.Rating)
        }).ToListAsync();

        return raw.Select(x => new EnrollmentListDto(
            x.Id, x.CourseId, x.Code, x.Title, x.ThumbnailUrl,
            x.UserId, x.UserFullName, x.EmployeeCode, x.DepartmentName,
            x.Status.ToString(), x.ProgressPercent, x.FinalScore,
            x.AssignedAt, x.DueDate, x.StartedAt, x.CompletedAt,
            x.DueDate.HasValue && x.DueDate < now && x.Status != EnrollmentStatus.Completed,
            x.HasCertificate,
            CourseRating.Round(x.AverageRating), x.ReviewCount)).ToList();
    }
}
