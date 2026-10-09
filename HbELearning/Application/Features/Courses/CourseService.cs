// File: Application/Features/Courses/CourseService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Courses;

public class CourseService : ICourseService
{
    private readonly IAppDbContext _db;
    private readonly INotificationDispatcher _notificationService;
    private readonly ICurrentUser _currentUser;
    private readonly IScormPackageStorage _scormStorage;

    public CourseService(IAppDbContext db, INotificationDispatcher notificationService, ICurrentUser currentUser,
        IScormPackageStorage scormStorage)
    {
        _db = db;
        _notificationService = notificationService;
        _currentUser = currentUser;
        _scormStorage = scormStorage;
    }

    public async Task<Result> GetList(string? keyword, CourseStatus? status, int? departmentId, int? positionId, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Course.AsNoTracking().AsQueryable();

        // Người được Giám Đốc Nhân sự ủy quyền duyệt bước cuối thấy thêm khóa học đang chờ Giám Đốc Nhân sự
        // duyệt / đã bị từ chối ở bước này, y hệt Giám Đốc Nhân sự — bất kể vai trò của họ là gì.
        var isDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId);
        // Chỉ chính Giám Đốc Nhân sự mới được xem tên thật của người được ủy quyền đã xử lý bước cuối — dùng
        // để tính nhãn hiển thị đúng quyền riêng tư bên dưới (ResolveHrApproverLabel).
        var viewerIsHrDirector = await IsHrDirectorAsync();
        // Tên người mình đang ủy quyền — hiện ngay trên thanh lộ trình của những bài chưa duyệt xong bước cuối.
        var activeDelegateName = viewerIsHrDirector ? await GetActiveDelegateNameAsync() : null;
        var viewer = await GetViewerAsync();

        // Toàn bộ quy tắc "ai thấy khóa học nào" gộp trong MỘT biểu thức thay vì mỗi vai trò một nhánh như trước:
        // điều kiện cuối ("đang dừng ở đúng bước của luồng cấp duyệt giao cho chính người này") áp cho MỌI vai trò,
        // mà một bước của luồng có thể giao cho phòng ban khác hẳn phòng ban của người tạo — tách nhánh thì phải
        // chép lại nguyên điều kiện đó vào từng nhánh, và sớm muộn sẽ có nhánh bị quên.
        if (!_currentUser.IsAdmin)
        {
            var isDepartmentApprover = _currentUser.IsHeadOfDepartment || _currentUser.IsDirector;
            var myDepartmentId = _currentUser.DepartmentId;
            var myRoleId = viewer.RoleId;
            var myParentRoleId = viewer.ParentRoleId;
            var myUserId = _currentUser.UserId;

            query = query.Where(x =>
                x.Status == CourseStatus.Published
                // Khóa học do chính mình soạn.
                || x.CreatedById == _currentUser.UserId
                // Trưởng phòng / Giám Đốc theo dõi mọi khóa học của phòng mình, ở mọi trạng thái.
                || (isDepartmentApprover && myDepartmentId != null && x.CreatedBy.DepartmentId == myDepartmentId)
                // Giám Đốc Nhân sự (và người được ủy quyền) thấy khóa học đang chờ mình duyệt bước cuối hoặc
                // đã bị chính mình từ chối — chúng thường khác phòng ban nên không khớp điều kiện trên.
                || ((viewerIsHrDirector || isDelegate)
                    && (x.Status == CourseStatus.PendingHrApproval || x.Status == CourseStatus.Rejected))
                // Luồng cấp duyệt do Admin định nghĩa: thấy khóa học đang dừng ở đúng bước giao cho mình (đang
                // chờ duyệt, hoặc vừa bị từ chối tại bước đó nên vẫn cần theo dõi tiếp).
                // Người được CHỈ ĐỊNH đích danh duyệt bước đang dừng — Admin ghim sẵn trong luồng (FixedUserId)
                // hoặc người soạn chọn lúc gửi duyệt (bảng CourseStepApprover). Họ có thể ở phòng ban khác hẳn
                // nên không khớp điều kiện vai trò + phòng ban bên dưới, mà vẫn phải thấy bài để bấm Duyệt.
                || ((x.Status == CourseStatus.PendingApproval || x.Status == CourseStatus.Rejected)
                    && x.ApprovalWorkflowId != null
                    && (_db.CourseStepApprover.Any(a => a.CourseId == x.Id
                            && a.StepOrder == x.CurrentStepOrder && a.ApproverUserId == myUserId)
                        || _db.ApprovalWorkflowStep.Any(s =>
                            s.ApprovalWorkflowId == x.ApprovalWorkflowId
                            && s.OrderIndex == x.CurrentStepOrder
                            && ((s.AllowDelegate && isDelegate)
                                || s.FixedUserId == myUserId
                                || ((s.RoleId == myRoleId || s.RoleId == myParentRoleId)
                                    && (s.DepartmentId == null
                                        ? x.CreatedBy.DepartmentId == myDepartmentId
                                        : s.DepartmentId == myDepartmentId)))))));
        }

        if (status.HasValue) query = query.Where(x => x.Status == status);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(k) || x.Code.ToLower().Contains(k));
        }
        if (departmentId.HasValue)
            query = query.Where(x => x.CourseTargetList.Any(t => t.DepartmentId == departmentId));
        if (positionId.HasValue)
            query = query.Where(x => x.CourseTargetList.Any(t => t.PositionId == positionId));

        var total = await query.CountAsync();

        var raw = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Code, x.Title, x.Description, x.ThumbnailUrl, x.Status,
                x.DurationMinutes, x.PassScore, x.IsMandatory,
                LessonCount = x.LessonList.Count(l => l.IsActive),
                QuizCount = x.QuizList.Count(q => q.IsActive),
                ScormPackageCount = x.ScormPackageList.Count(p => p.Kind == ScormPackageKind.Imported),
                EnrollmentCount = x.EnrollmentList.Count,
                x.CreatedById,
                CreatedByName = x.CreatedBy.FullName,
                CreatedByDepartmentId = x.CreatedBy.DepartmentId,
                x.CreatedAt,
                x.StartDate,
                x.EndDate,
                x.RejectionReason,
                x.RejectedByRoleName,
                x.HrApproverUserId,
                x.HrApproverIsDelegate,
                HrApproverName = x.HrApprover != null ? x.HrApprover.FullName : null,
                x.ApprovalWorkflowId,
                ApprovalWorkflowName = x.ApprovalWorkflow != null ? x.ApprovalWorkflow.Name : null,
                x.CurrentStepOrder,
                // Các bước của luồng cấp duyệt đính kèm từng khóa học — giao diện cần chúng để vẽ thanh tiến trình,
                // còn ở đây dùng để tính sẵn cờ "người này có được duyệt bước hiện tại không" (CanApprove).
                StepList = _db.ApprovalWorkflowStep
                    .Where(s => s.ApprovalWorkflowId == x.ApprovalWorkflowId)
                    .OrderBy(s => s.OrderIndex)
                    .Select(s => new { s.OrderIndex, s.Name, s.RoleId, s.DepartmentId, s.AllowDelegate,
                                       s.ApproverMode, s.FixedUserId })
                    .ToList(),
                // Người soạn đã chỉ định ai duyệt bước đang dừng (chỉ có với bước ApproverMode = PickOne).
                AssignedUserId = _db.CourseStepApprover
                    .Where(a => a.CourseId == x.Id && a.StepOrder == x.CurrentStepOrder)
                    .Select(a => (int?)a.ApproverUserId)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Tên người duyệt đích danh của từng khóa học, tra MỘT lần cho cả trang. Đưa vào câu truy vấn trên
        // dưới dạng truy vấn con thì mỗi khóa học tốn thêm một lần đọc bảng người dùng.
        var designatedNameMap = await GetUserNameMapAsync(raw
            .Where(x => x.Status == CourseStatus.PendingApproval)
            .Select(x => ResolveDesignatedUserId(
                x.StepList.Where(s => s.OrderIndex == x.CurrentStepOrder)
                    .Select(s => new WorkflowStepInfo(s.OrderIndex, s.Name, s.RoleId, s.DepartmentId,
                        s.AllowDelegate, s.ApproverMode, s.FixedUserId))
                    .FirstOrDefault(),
                x.AssignedUserId))
            .ToList());

        // Ai đã từ chối — chỉ tra khi người xem là Giám Đốc Nhân sự, vì chỉ họ mới được hiển thị tên này.
        var rejectedByNameMap = viewerIsHrDirector
            ? await GetRejectedByNameMapAsync(
                raw.Where(x => x.Status == CourseStatus.Rejected).Select(x => x.Id).ToList())
            : new Dictionary<int, string>();

        var itemList = raw.Select(x =>
        {
            var stepList = x.StepList
                .Select(s => new WorkflowStepInfo(s.OrderIndex, s.Name, s.RoleId, s.DepartmentId, s.AllowDelegate,
                    s.ApproverMode, s.FixedUserId))
                .ToList();
            var currentStep = stepList.FirstOrDefault(s => s.OrderIndex == x.CurrentStepOrder);
            var designatedUserId = x.Status == CourseStatus.PendingApproval
                ? ResolveDesignatedUserId(currentStep, x.AssignedUserId)
                : null;

            return new CourseListDto(
                x.Id, x.Code, x.Title, x.Description, x.ThumbnailUrl, x.Status.ToString(),
                x.DurationMinutes, x.PassScore, x.IsMandatory,
                x.LessonCount, x.QuizCount, x.EnrollmentCount,
                x.CreatedById, x.CreatedByName, x.CreatedByDepartmentId, x.CreatedAt,
                x.StartDate, x.EndDate,
                x.RejectionReason, x.RejectedByRoleName,
                ResolveHrApproverLabel(x.HrApproverUserId, x.HrApproverIsDelegate, x.HrApproverName,
                    viewerIsHrDirector, activeDelegateName),
                x.ApprovalWorkflowId, x.ApprovalWorkflowName, x.CurrentStepOrder,
                stepList.Select(s => s.Name).ToList(),
                CanApproveWorkflowStep(x.Status, x.CurrentStepOrder, x.CreatedByDepartmentId, stepList, viewer,
                    isDelegate, x.AssignedUserId),
                ResolveCurrentStepApproverName(x.Status, currentStep,
                    designatedUserId.HasValue ? designatedNameMap.GetValueOrDefault(designatedUserId.Value) : null,
                    activeDelegateName, rejectedByNameMap.GetValueOrDefault(x.Id)),
                CanReassignApprover(x.Status, currentStep),
                ScormPackageCount: x.ScormPackageCount);
        }).ToList();

        return Result.Success(new PagedResult<CourseListDto>(itemList, total, page, pageSize));
    }

    public async Task<Result> GetById(int id)
    {
        var course = await _db.Course.AsNoTracking()
            .Include(x => x.CreatedBy)
            .Include(x => x.HrApprover)
            .Include(x => x.ApprovalWorkflow!).ThenInclude(w => w.StepList)
            .Include(x => x.CourseTargetList).ThenInclude(t => t.Department)
            .Include(x => x.CourseTargetList).ThenInclude(t => t.Position)
            .Include(x => x.LessonList)
            .Include(x => x.QuizList).ThenInclude(q => q.QuestionList)
            .Include(x => x.ScenarioSceneList).ThenInclude(s => s.NodeList)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (!await CanViewCourseAsync(course))
            return Result.Forbidden();

        var targetList = course.CourseTargetList
            .Select(t => new CourseTargetDto(t.Id, t.DepartmentId, t.Department?.Name, t.PositionId, t.Position?.Name))
            .ToList();

        var lessonList = course.LessonList.Where(l => l.IsActive).OrderBy(l => l.OrderIndex)
            .Select(l =>
            {
                var childQuiz = course.QuizList.FirstOrDefault(q => q.LessonId == l.Id && q.IsActive);
                var childScenario = course.ScenarioSceneList.FirstOrDefault(s => s.LessonId == l.Id && s.IsActive);
                return new LessonListDto(l.Id, l.CourseId, l.Title, l.ContentType.ToString(),
                    l.ResourceUrl, l.DurationMinutes, l.OrderIndex, l.IsActive, childQuiz?.Id, childQuiz?.Title,
                    childScenario?.Id, childScenario?.Title, childScenario?.NodeList.Count ?? 0);
            })
            .ToList();

        var quizList = course.QuizList.Where(q => q.IsActive)
            .Select(q => new QuizListDto(q.Id, q.CourseId, q.Title, q.PassScore, q.TimeLimitMinutes,
                q.MaxAttempt, q.IsFinalExam, q.QuestionList.Count, q.IsActive,
                q.LessonId, q.LessonId.HasValue ? course.LessonList.FirstOrDefault(l => l.Id == q.LessonId)?.Title : null))
            .ToList();

        var viewerIsHrDirector = await IsHrDirectorAsync();
        // Tên người mình đang ủy quyền — dùng cho cả nhãn bước cuối của chuỗi mặc định lẫn tên "đang chờ ai"
        // của bước cho phép ủy quyền trong luồng cấp duyệt.
        var activeDelegateName = viewerIsHrDirector ? await GetActiveDelegateNameAsync() : null;
        var hrApproverLabel = ResolveHrApproverLabel(
            course.HrApproverUserId, course.HrApproverIsDelegate, course.HrApprover?.FullName, viewerIsHrDirector,
            activeDelegateName);

        var stepList = (course.ApprovalWorkflow?.StepList ?? new List<ApprovalWorkflowStep>())
            .OrderBy(s => s.OrderIndex)
            .Select(s => new WorkflowStepInfo(s.OrderIndex, s.Name, s.RoleId, s.DepartmentId, s.AllowDelegate,
                s.ApproverMode, s.FixedUserId))
            .ToList();

        // Người được chỉ định duyệt BƯỚC ĐANG CHỜ (nếu bước đó thuộc loại chỉ định đích danh) — vừa để tính
        // nút Duyệt, vừa để hiển thị "đang chờ ai" trên thanh tiến trình.
        var assignedUserId = await _db.CourseStepApprover.AsNoTracking()
            .Where(x => x.CourseId == course.Id && x.StepOrder == course.CurrentStepOrder)
            .Select(x => (int?)x.ApproverUserId)
            .FirstOrDefaultAsync();
        var currentStep = stepList.FirstOrDefault(s => s.OrderIndex == course.CurrentStepOrder);
        var designatedUserId = ResolveDesignatedUserId(currentStep, assignedUserId);
        var designatedName = designatedUserId.HasValue && course.Status == CourseStatus.PendingApproval
            ? await _db.User.AsNoTracking().Where(x => x.Id == designatedUserId)
                .Select(x => x.FullName).FirstOrDefaultAsync()
            : null;
        designatedName = ResolveCurrentStepApproverName(course.Status, currentStep, designatedName, activeDelegateName,
            viewerIsHrDirector && course.Status == CourseStatus.Rejected
                ? (await GetRejectedByNameMapAsync(new[] { course.Id })).GetValueOrDefault(course.Id)
                : null);

        var canApprove = CanApproveWorkflowStep(course.Status, course.CurrentStepOrder,
            course.CreatedBy.DepartmentId, stepList, await GetViewerAsync(),
            await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId),
            assignedUserId);

        return Result.Success(new CourseDetailDto(
            course.Id, course.Code, course.Title, course.Description, course.ThumbnailUrl,
            course.Status.ToString(), course.DurationMinutes, course.PassScore, course.IsMandatory,
            course.DeadlineDay, course.StartDate, course.EndDate,
            course.CreatedById, course.CreatedBy.FullName, course.CreatedBy.DepartmentId,
            course.CreatedAt, course.UpdatedAt, course.RejectionReason, course.RejectedByRoleName, hrApproverLabel,
            targetList, lessonList, quizList,
            course.ApprovalWorkflowId, course.ApprovalWorkflow?.Name, course.CurrentStepOrder,
            stepList.Select(s => s.Name).ToList(), canApprove,
            designatedName, CanReassignApprover(course.Status, currentStep)));
    }

    public async Task<Result> GetMyAvailable()
    {
        var departmentId = _currentUser.DepartmentId;
        var positionId = _currentUser.PositionId;

        var enrolledIdList = await _db.Enrollment
            .Where(e => e.UserId == _currentUser.UserId)
            .Select(e => e.CourseId)
            .ToListAsync();
        var viewerIsHrDirector = await IsHrDirectorAsync();

        // Khóa đã từng phân công rồi bị hủy vẫn còn bản ghi Enrollment nên cũng bị loại — người phụ trách đã
        // chủ động gỡ thì không gợi ý lại cho học viên tự đăng ký.
        var raw = await _db.Course.AsNoTracking()
            .OpenFor(departmentId, positionId)
            .Where(x => !enrolledIdList.Contains(x.Id))
            .Select(x => new
            {
                MatchDepartment = departmentId != null && x.CourseTargetList.Any(t => t.DepartmentId == departmentId),
                MatchPosition = positionId != null && x.CourseTargetList.Any(t => t.PositionId == positionId),
                x.Id, x.Code, x.Title, x.Description, x.ThumbnailUrl, x.Status,
                x.DurationMinutes, x.PassScore, x.IsMandatory,
                LessonCount = x.LessonList.Count(l => l.IsActive),
                QuizCount = x.QuizList.Count(q => q.IsActive),
                ScormPackageCount = x.ScormPackageList.Count(p => p.Kind == ScormPackageKind.Imported),
                EnrollmentCount = x.EnrollmentList.Count,
                x.CreatedById,
                CreatedByName = x.CreatedBy.FullName,
                CreatedByDepartmentId = x.CreatedBy.DepartmentId,
                x.CreatedAt,
                x.StartDate,
                x.EndDate,
                x.RejectionReason,
                x.RejectedByRoleName,
                x.HrApproverUserId,
                x.HrApproverIsDelegate,
                HrApproverName = x.HrApprover != null ? x.HrApprover.FullName : null
            })
            .ToListAsync();

        // Xếp như một danh sách gợi ý: khóa nhắm đúng phòng ban + vị trí của học viên lên đầu, rồi khớp phòng
        // ban, rồi khớp vị trí, khóa dành cho tất cả xếp cuối; cùng mức thì khóa bắt buộc trước, mới trước cũ.
        var list = raw
            .OrderByDescending(x => (x.MatchDepartment ? 2 : 0) + (x.MatchPosition ? 1 : 0))
            .ThenByDescending(x => x.IsMandatory)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new CourseListDto(
            x.Id, x.Code, x.Title, x.Description, x.ThumbnailUrl, x.Status.ToString(),
            x.DurationMinutes, x.PassScore, x.IsMandatory,
            x.LessonCount, x.QuizCount, x.EnrollmentCount,
            x.CreatedById, x.CreatedByName, x.CreatedByDepartmentId, x.CreatedAt,
            x.StartDate, x.EndDate,
            x.RejectionReason, x.RejectedByRoleName,
            ResolveHrApproverLabel(x.HrApproverUserId, x.HrApproverIsDelegate, x.HrApproverName, viewerIsHrDirector),
            SuggestReason: x.MatchDepartment && x.MatchPosition ? "DepartmentPosition"
                : x.MatchDepartment ? "Department"
                : x.MatchPosition ? "Position"
                : "All",
            ScormPackageCount: x.ScormPackageCount)).ToList();

        return Result.Success(list);
    }

    public async Task<Result> Create(CourseCreateRequest request)
    {
        // Khóa học dùng chung toàn công ty không thuộc phòng ban nào, nên lấy tiền tố ALL. Không đụng tới
        // danh sách đối tượng đào tạo: khóa mới tạo vốn chưa có dòng đối tượng nào, mà "không có dòng nào"
        // đã là quy ước "áp dụng cho toàn công ty" của hệ thống (xem GetMyAvailable và
        // GetCourseAudienceUserIdListAsync). Thêm đủ 39 dòng phòng ban vào đây lại hóa ra HẸP hơn — nhân
        // viên chưa được gán phòng ban sẽ không khớp dòng nào và mất luôn khóa học.
        string codePrefix;
        if (request.AllDepartment)
        {
            codePrefix = AllDepartmentCodePrefix;
        }
        else
        {
            var department = await _db.Department.FirstOrDefaultAsync(x => x.Id == request.DepartmentId);
            if (department is null) return Result.Invalid(new MessageResponse("Phòng ban không hợp lệ."));
            codePrefix = department.Code;
        }

        if (!TryGetCoursePeriod(request, out var startDate, out var endDate))
            return Result.Invalid(new MessageResponse(InvalidCoursePeriodMessage));

        var workflowId = await ResolveWorkflowIdAsync(request.ApprovalWorkflowId);
        if (request.ApprovalWorkflowId.HasValue && workflowId is null)
            return Result.Invalid(new MessageResponse("Luồng cấp duyệt không hợp lệ hoặc đã ngừng hoạt động."));

        var code = await GenerateCourseCode(codePrefix);

        var course = new Course
        {
            Code = code,
            Title = request.Title.Trim(),
            Description = request.Description,
            ThumbnailUrl = request.ThumbnailUrl,
            DurationMinutes = request.DurationMinutes,
            PassScore = request.PassScore,
            IsMandatory = request.IsMandatory,
            DeadlineDay = request.DeadlineDay,
            StartDate = startDate,
            EndDate = endDate,
            CreatedById = _currentUser.UserId,
            Status = CourseStatus.Draft,
            ApprovalWorkflowId = workflowId
        };

        _db.Course.Add(course);
        await _db.SaveChangesAsync();

        return Result.Created(nameof(GetById), new { id = course.Id }, new MessageResponse($"Đã tạo khóa học #{course.Id} với mã {code}.", course.Id));
    }

    /// <summary>Tiền tố mã cho khóa học dùng chung toàn công ty, thay cho mã phòng ban.</summary>
    private const string AllDepartmentCodePrefix = "ALL";

    private const string InvalidCoursePeriodMessage = "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.";

    /// <summary>Khung thời gian của khóa học đã cắt về phần ngày. Trả false khi ngày kết thúc đứng trước ngày bắt
    /// đầu — ô trống thì [Required] ở CourseRequest đã chặn từ trước, tới đây cả hai đều có giá trị.</summary>
    private static bool TryGetCoursePeriod(CourseRequest request, out DateTime startDate, out DateTime endDate)
    {
        startDate = request.StartDate!.Value.Date;
        endDate = request.EndDate!.Value.Date;
        return endDate >= startDate;
    }

    /// <summary>Tên chuẩn của bước duyệt cuối. Vừa là nhãn hiển thị, vừa là giá trị ghi vào RejectedByRoleName
    /// — giao diện dò đúng bước bị từ chối bằng cách so chuỗi này, nên đổi ở đây là đổi cả hai nơi.</summary>
    private const string HrStepLabel = "Giám Đốc Nhân sự";

    /// <summary>Sinh mã khóa học dạng {MãPhòngBan}-{Số thứ tự 3 chữ số}, ví dụ AT-001.</summary>
    private async Task<string> GenerateCourseCode(string departmentCode)
    {
        var prefix = departmentCode.Trim().ToUpper();
        var seq = await _db.Course.CountAsync(x => x.Code.StartsWith(prefix + "-")) + 1;
        var code = $"{prefix}-{seq:D3}";
        while (await _db.Course.AnyAsync(x => x.Code == code))
        {
            seq++;
            code = $"{prefix}-{seq:D3}";
        }
        return code;
    }

    public async Task<Result> Update(int id, CourseRequest request)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (course.CreatedById != _currentUser.UserId) return Result.Forbidden();
        if (!TryGetCoursePeriod(request, out var startDate, out var endDate))
            return Result.Invalid(new MessageResponse(InvalidCoursePeriodMessage));

        course.Title = request.Title.Trim();
        course.Description = request.Description;
        course.ThumbnailUrl = request.ThumbnailUrl;
        course.DurationMinutes = request.DurationMinutes;
        course.PassScore = request.PassScore;
        course.IsMandatory = request.IsMandatory;
        course.DeadlineDay = request.DeadlineDay;
        course.StartDate = startDate;
        course.EndDate = endDate;

        // Đổi luồng cấp duyệt giữa chừng sẽ làm CurrentStepOrder trỏ sang một bước hoàn toàn khác của luồng mới —
        // khóa học đang chờ Trưởng phòng bỗng nhảy sang chờ Giám Đốc mà không ai gửi duyệt lại. Chỉ cho đổi khi khóa
        // học còn ở đầu chuỗi (Nháp / Từ chối), lúc đó gửi duyệt sẽ chạy lại từ bước 1.
        if (request.ApprovalWorkflowId != course.ApprovalWorkflowId)
        {
            if (course.Status != CourseStatus.Draft && course.Status != CourseStatus.Rejected)
                return Result.Invalid(new MessageResponse("Khóa học đã gửi duyệt, không đổi được luồng cấp duyệt."));

            var workflowId = await ResolveWorkflowIdAsync(request.ApprovalWorkflowId);
            if (request.ApprovalWorkflowId.HasValue && workflowId is null)
                return Result.Invalid(new MessageResponse("Luồng cấp duyệt không hợp lệ hoặc đã ngừng hoạt động."));

            course.ApprovalWorkflowId = workflowId;
            // Người duyệt đã được ĐỔI giữa chừng (ReassignApprover) trỏ theo SỐ THỨ TỰ bước của luồng cũ —
            // bước 2 của luồng này với bước 2 của luồng kia là hai cấp duyệt khác hẳn nhau. Đổi luồng là dọn.
            ClearStepApprover(course.Id);
        }

        course.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    public async Task<Result> ChangeApprovalWorkflow(int id, int? workflowId)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (course.CreatedById != _currentUser.UserId) return Result.Forbidden();

        // Cùng ràng buộc với Update: đổi luồng giữa chừng làm CurrentStepOrder trỏ sang một bước khác hẳn
        // của luồng mới, khóa học đang chờ cấp này bỗng nhảy sang cấp khác mà không ai gửi duyệt lại.
        if (!course.CanBeSubmitted)
            return Result.Invalid(new MessageResponse("Khóa học đã gửi duyệt, không đổi được luồng cấp duyệt."));

        var resolvedId = await ResolveWorkflowIdAsync(workflowId);
        if (workflowId.HasValue && resolvedId is null)
            return Result.Invalid(new MessageResponse("Luồng cấp duyệt không hợp lệ hoặc đã ngừng hoạt động."));

        course.ChangeApprovalWorkflow(resolvedId, DateTime.Now);
        ClearStepApprover(course.Id);
        await _db.SaveChangesAsync();

        if (resolvedId is null)
            return Result.Success(new MessageResponse("Khóa học sẽ chạy chuỗi duyệt mặc định."));

        var name = await _db.ApprovalWorkflow.AsNoTracking()
            .Where(x => x.Id == resolvedId).Select(x => x.Name).FirstAsync();
        return Result.Success(new MessageResponse($"Khóa học sẽ duyệt theo luồng \"{name}\"."));
    }

    public async Task<Result> ChangeStatus(int id, CourseStatus status)
    {
        // Chỉ đúng hai đích đến: Lưu trữ (cất khóa học đã xuất bản đi) và Bản nháp (bỏ lưu trữ, đưa nội dung
        // quay lại đầu chuỗi duyệt). Đặt thẳng Published ở đây là xuất bản mà không qua Trưởng phòng ->
        // Giám Đốc -> Giám Đốc Nhân sự, đúng thứ mà chú thích trên đã hứa là không làm.
        if (status != CourseStatus.Archived && status != CourseStatus.Draft)
            return Result.Invalid(new MessageResponse(
                "Chỉ được chuyển khóa học sang Lưu trữ hoặc Bản nháp. Việc xuất bản phải đi qua chuỗi duyệt."));

        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        if (status == CourseStatus.Draft)
        {
            course.ReturnToDraft(DateTime.Now);
            ClearStepApprover(course.Id);
        }
        else
        {
            course.Archive(DateTime.Now);
        }
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse($"Khóa học đã chuyển sang trạng thái {status.ToVietnameseLabel()}."));
    }

    public async Task<Result> Submit(int id)
    {
        var course = await _db.Course.Include(x => x.CreatedBy).Include(x => x.LessonList).FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (course.CreatedById != _currentUser.UserId) return Result.Forbidden();
        if (!course.CanBeSubmitted)
            return Result.Invalid(new MessageResponse("Chỉ có thể gửi duyệt khóa học đang ở trạng thái Nháp hoặc Từ chối."));
        if (!course.LessonList.Any(l => l.IsActive))
            return Result.Invalid(new MessageResponse("Khóa học chưa có bài học nào, không thể gửi duyệt."));

        var now = DateTime.Now;
        var fromStatus = course.Status;

        // Giảng Viên xuất bản trực tiếp mà không qua chuỗi duyệt
        if (_currentUser.IsInstructor)
        {
            course.PublishDirectly(now);
            _db.CourseApprovalHistory.Add(new CourseApprovalHistory
            {
                CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
                StepName = "Xuất bản trực tiếp", FromStatus = fromStatus, ToStatus = course.Status
            });
            await _db.SaveChangesAsync();

            var publishRecipientList = await GetCourseAudienceUserIdListAsync(course);
            publishRecipientList.Add(_currentUser.UserId);
            await _notificationService.NotifyCourseWorkflowAsync(course.Id, publishRecipientList.Distinct().ToList(),
                NotificationType.CoursePublished, "Khóa học đã được xuất bản",
                $"Khóa học \"{course.Title}\" của {course.CreatedBy.FullName} đã được xuất bản.");

            return Result.Success(new MessageResponse("Khóa học đã được xuất bản."));
        }

        // Khóa học có chọn luồng cấp duyệt thì đi theo đúng các bước của luồng đó; không chọn thì chạy chuỗi duyệt
        // cố định Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự như trước.
        var workflowStepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
        if (course.ApprovalWorkflowId.HasValue && workflowStepList.Count == 0)
            return Result.Invalid(new MessageResponse(
                "Luồng cấp duyệt của khóa học chưa có bước nào, không thể gửi duyệt. Hãy chọn luồng khác hoặc báo Admin bổ sung bước."));

        if (workflowStepList.Count > 0)
        {
            var firstStep = workflowStepList[0];
            course.SubmitToWorkflow(firstStep.OrderIndex, now);

            // Chuỗi duyệt bắt đầu lại từ bước 1, nên mọi lần ĐỔI người duyệt giữa chừng (ReassignApprover)
            // của lần gửi trước đều hết hiệu lực — khóa học bị từ chối rồi gửi lại phải chạy đúng người mà
            // luồng đang quy định, chứ không phải người ai đó đã đổi sang ở vòng trước.
            ClearStepApprover(course.Id);

            _db.CourseApprovalHistory.Add(new CourseApprovalHistory
            {
                CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
                StepName = firstStep.Name, FromStatus = fromStatus, ToStatus = course.Status
            });
            await _db.SaveChangesAsync();

            // Không truyền người được chỉ định: vừa dọn sạch bảng chỉ định ở trên, người duyệt bước đầu (nếu
            // có) là người mà luồng ghim sẵn — ResolveDesignatedUserId tự lấy từ định nghĩa bước.
            await NotifyStepApproverAsync(course, firstStep, NotificationType.CourseSubmittedForApproval,
                "Có nội dung khóa học chờ duyệt",
                $"Khóa học \"{course.Title}\" cần bạn duyệt ở bước \"{firstStep.Name}\".",
                assignedUserId: null);

            var firstApproverName = await ResolveDesignatedNameAsync(firstStep, assignedUserId: null);
            return Result.Success(new MessageResponse(firstApproverName is null
                ? $"Đã gửi khóa học sang bước \"{firstStep.Name}\"."
                : $"Đã gửi khóa học sang bước \"{firstStep.Name}\" cho {firstApproverName} duyệt."));
        }

        course.SubmitToFixedChain(now);

        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
            FromStatus = fromStatus, ToStatus = course.Status
        });
        await _db.SaveChangesAsync();

        var recipientIdList = await GetUserIdListByRoleAsync(RoleName.HeadOfDepartment, course.CreatedBy.DepartmentId);
        await _notificationService.NotifyCourseWorkflowAsync(course.Id, recipientIdList, NotificationType.CourseSubmittedForApproval,
            "Có nội dung khóa học chờ duyệt", $"Khóa học \"{course.Title}\" cần bạn duyệt.");

        return Result.Success(new MessageResponse("Đã gửi khóa học cho Trưởng phòng duyệt."));
    }

    public async Task<Result> Approve(int id)
    {
        var course = await _db.Course.Include(x => x.CreatedBy).Include(x => x.LessonList)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        // Khóa học chạy theo luồng cấp duyệt do Admin định nghĩa có nhánh xử lý riêng: số bước và cấp duyệt của
        // từng bước do dữ liệu quyết định, không suy được từ trạng thái như chuỗi duyệt cố định bên dưới.
        if (course.IsOnWorkflowStep)
            return await ApproveWorkflowStepAsync(course);

        var sameDepartment = _currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == course.CreatedBy.DepartmentId;
        var fromStatus = course.Status;
        List<int> recipientIdList;
        NotificationType notifyType;
        string notifyTitle, notifyMessage;

        if (_currentUser.IsHeadOfDepartment)
        {
            if (course.Status != CourseStatus.PendingHeadApproval)
                return Result.Invalid(new MessageResponse("Khóa học không ở trạng thái chờ Trưởng phòng duyệt."));
            if (!sameDepartment) return Result.Forbidden();

            course.ApproveByHeadOfDepartment(DateTime.Now);
            recipientIdList = await GetUserIdListByRoleAsync(RoleName.Director, course.CreatedBy.DepartmentId);
            notifyType = NotificationType.CourseApproved;
            notifyTitle = "Có nội dung khóa học chờ duyệt";
            notifyMessage = $"Khóa học \"{course.Title}\" đã được Trưởng phòng duyệt, đang chờ bạn duyệt.";
        }
        else if (_currentUser.IsDirector && course.Status == CourseStatus.PendingDirectorApproval)
        {
            if (!sameDepartment) return Result.Forbidden();

            course.ApproveByDirector(DateTime.Now);
            var hrDepartmentId = await GetHrDepartmentIdAsync();
            recipientIdList = await GetUserIdListByRoleAsync(RoleName.Director, hrDepartmentId);
            notifyType = NotificationType.CourseApproved;
            notifyTitle = "Có nội dung khóa học chờ duyệt";
            notifyMessage = $"Khóa học \"{course.Title}\" đã được Giám Đốc phòng ban duyệt, đang chờ Giám Đốc Nhân sự duyệt xuất bản.";
        }
        else if (course.Status == CourseStatus.PendingHrApproval)
        {
            var (allowed, isHrDirectorApprover) = await GetHrApprovalPermissionAsync();
            if (!allowed) return Result.Forbidden();
            if (!course.LessonList.Any(l => l.IsActive))
                return Result.Invalid(new MessageResponse("Khóa học chưa có bài học nào, không thể xuất bản."));

            // Ghi rõ ai thực sự duyệt bước cuối — chỉ Giám Đốc Nhân sự được xem tên người được ủy quyền, các
            // vai trò khác luôn thấy nhãn "Giám Đốc Nhân sự" (CourseService tự tính khi trả dữ liệu ở GetList/GetById).
            course.PublishByHr(_currentUser.UserId, !isHrDirectorApprover, DateTime.Now);
            recipientIdList = await GetCourseAudienceUserIdListAsync(course);
            recipientIdList.Add(course.CreatedById);
            notifyType = NotificationType.CoursePublished;
            notifyTitle = "Khóa học đã được xuất bản";
            notifyMessage = $"Khóa học \"{course.Title}\" đã được duyệt qua tất cả các cấp và chính thức xuất bản.";
        }
        else
        {
            return Result.Forbidden();
        }

        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
            FromStatus = fromStatus, ToStatus = course.Status
        });
        await _db.SaveChangesAsync();

        await _notificationService.NotifyCourseWorkflowAsync(course.Id, recipientIdList, notifyType, notifyTitle, notifyMessage);

        return Result.Success(new MessageResponse(course.Status == CourseStatus.Published
            ? "Đã duyệt và xuất bản khóa học."
            : $"Đã duyệt, chuyển khóa học sang trạng thái {course.Status.ToVietnameseLabel()}."));
    }

    public async Task<Result> Reject(int id, CourseRejectRequest request)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Invalid(new MessageResponse("Vui lòng nhập lý do từ chối."));

        var course = await _db.Course.Include(x => x.CreatedBy).FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        if (course.IsOnWorkflowStep)
            return await RejectWorkflowStepAsync(course, reason);

        var sameDepartment = _currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == course.CreatedBy.DepartmentId;
        var hrDepartmentId = await GetHrDepartmentIdAsync();
        var isHrDirector = _currentUser.IsDirector && _currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == hrDepartmentId;
        var isHrDelegate = course.Status == CourseStatus.PendingHrApproval
            && await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId);

        var allowed =
            (_currentUser.IsHeadOfDepartment && course.Status == CourseStatus.PendingHeadApproval && sameDepartment) ||
            (_currentUser.IsDirector && course.Status == CourseStatus.PendingDirectorApproval && sameDepartment) ||
            (course.Status == CourseStatus.PendingHrApproval && (isHrDirector || isHrDelegate));
        if (!allowed) return Result.Forbidden();

        // "Giám Đốc" là tên vai trò dùng chung cho cả Giám Đốc phòng ban lẫn Giám Đốc Nhân sự — nếu chỉ lưu
        // CurrentRole thì không phân biệt được khóa học bị từ chối ở bước 2 (phòng ban) hay bước 3 (Nhân sự,
        // bước cuối). RejectedByRoleName luôn dùng tên bước chuẩn ("Giám Đốc Nhân sự") kể cả khi người được
        // ủy quyền từ chối thay — chỉ Giám Đốc Nhân sự mới thấy đúng người đã từ chối, qua HrApproverUserId/
        // HrApproverIsDelegate (CourseService tự tính nhãn hiển thị khi trả dữ liệu ở GetList/GetById).
        //
        // Cũng KHÔNG dùng CurrentRole cho hai bước còn lại: vai trò kế thừa mang tên riêng ("Trưởng phòng IT"),
        // ghi tên đó vào đây thì thanh tiến trình dò không ra bước nào bị từ chối và hiện sai toàn bộ. Suy từ
        // chính bước vừa xử lý — đó mới là thứ giao diện cần biết.
        var rejectedByLabel = (isHrDirector || isHrDelegate) ? HrStepLabel
            : course.Status == CourseStatus.PendingHeadApproval ? RoleName.HeadOfDepartment
            : RoleName.Director;

        var fromStatus = course.Status;
        course.Reject(reason, rejectedByLabel, DateTime.Now);
        if (isHrDirector || isHrDelegate)
            course.RecordHrDecisionMaker(_currentUser.UserId, isHrDelegate);

        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = rejectedByLabel,
            FromStatus = fromStatus, ToStatus = course.Status, Reason = reason
        });
        await _db.SaveChangesAsync();

        await _notificationService.NotifyCourseRejectedAsync(course.Id, rejectedByLabel, reason);
        if (isHrDelegate) await NotifyGranterOfRejectionAsync(course.Id, reason);

        return Result.Success(new MessageResponse("Đã từ chối nội dung, khóa học được chuyển sang trạng thái Từ chối."));
    }

    public async Task<Result> GetApprovalHistory(int id)
    {
        var course = await _db.Course.AsNoTracking().Include(x => x.CreatedBy).FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (!await CanViewCourseAsync(course))
            return Result.Forbidden();

        var list = await _db.CourseApprovalHistory.AsNoTracking()
            .Where(x => x.CourseId == id)
            .Include(x => x.ActorUser)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CourseApprovalHistoryDto(
                x.Id, x.ActorUserId, x.ActorUser.FullName, x.ActorRoleName,
                x.FromStatus.ToString(), x.ToStatus.ToString(), x.Reason, x.CreatedAt, x.StepName))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetStepApproverOptionList(int id)
    {
        var course = await _db.Course.AsNoTracking().Include(x => x.CreatedBy).FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var stepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
        if (stepList.Count == 0) return Result.Success(Array.Empty<CourseStepApproverOptionDto>());

        return Result.Success(await BuildApproverOptionListAsync(stepList, course.CreatedBy.DepartmentId,
            await GetAssignedApproverMapAsync(course.Id)));
    }

    /// <summary>Dựng danh sách "bước + những ai duyệt được bước đó" cho giao diện chọn người duyệt.</summary>
    private async Task<List<CourseStepApproverOptionDto>> BuildApproverOptionListAsync(
        IReadOnlyList<WorkflowStepInfo> stepList, int? creatorDepartmentId,
        IReadOnlyDictionary<int, int> assignmentMap)
    {
        var resultList = new List<CourseStepApproverOptionDto>();

        foreach (var step in stepList)
        {
            // Bước Admin đã ghim sẵn người duyệt thì "danh sách chọn" chỉ có đúng người đó — người soạn không
            // đổi được, giao diện chỉ hiển thị tên cho biết.
            var candidateQuery = step.ApproverMode == StepApproverMode.FixedUser
                ? _db.User.AsNoTracking().Where(x => x.Id == step.FixedUserId)
                : EligibleApproverQuery(step, creatorDepartmentId);

            var candidateList = await candidateQuery
                .OrderBy(x => x.FullName)
                .Select(x => new ApproverCandidateDto(x.Id, x.FullName, x.EmployeeCode, x.Role.Name,
                    x.Department != null ? x.Department.Name : null,
                    x.Position != null ? x.Position.Name : null))
                .ToListAsync();

            resultList.Add(new CourseStepApproverOptionDto(
                step.OrderIndex, step.Name, step.ApproverMode.ToString(),
                ResolveDesignatedUserId(step, AssignedUserIdOf(assignmentMap, step.OrderIndex)),
                candidateList));
        }

        return resultList;
    }

    public async Task<Result> ReassignApprover(int id, CourseReassignApproverRequest request)
    {
        var course = await _db.Course.Include(x => x.CreatedBy).FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (course.Status != CourseStatus.PendingApproval)
            return Result.Invalid(new MessageResponse("Chỉ đổi được người duyệt khi khóa học đang chờ duyệt."));

        if (request.UserId <= 0)
            return Result.Invalid(new MessageResponse("Vui lòng chọn người duyệt mới cho bước này."));

        var stepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
        var step = stepList.FirstOrDefault(x => x.OrderIndex == request.StepOrder);
        if (step is null) return Result.Invalid(new MessageResponse("Không tìm thấy bước duyệt cần đổi người."));
        if (step.ApproverMode == StepApproverMode.FixedUser)
            return Result.Invalid(new MessageResponse(
                $"Bước \"{step.Name}\" có người duyệt cố định do Admin đặt trong luồng cấp duyệt — đổi ở màn hình Luồng cấp duyệt."));
        if (request.StepOrder < course.CurrentStepOrder)
            return Result.Invalid(new MessageResponse($"Bước \"{step.Name}\" đã duyệt xong, không đổi được người duyệt."));

        if (!await EligibleApproverQuery(step, course.CreatedBy.DepartmentId).AnyAsync(x => x.Id == request.UserId))
            return Result.Invalid(new MessageResponse(
                $"Người bạn chọn không duyệt được bước \"{step.Name}\" (sai vai trò / phòng ban, hoặc tài khoản đã ngừng hoạt động)."));

        var row = await _db.CourseStepApprover
            .FirstOrDefaultAsync(x => x.CourseId == course.Id && x.StepOrder == request.StepOrder);
        if (row is not null && row.ApproverUserId == request.UserId)
            return Result.Success(new MessageResponse("Người duyệt của bước này không thay đổi."));

        var previousUserId = row?.ApproverUserId;
        if (row is null)
            _db.CourseStepApprover.Add(new CourseStepApprover
            {
                CourseId = course.Id, StepOrder = request.StepOrder,
                ApproverUserId = request.UserId, AssignedById = _currentUser.UserId
            });
        else
        {
            row.ApproverUserId = request.UserId;
            row.AssignedById = _currentUser.UserId;
            row.AssignedAt = DateTime.Now;
        }

        // Ghi vào lịch sử duyệt: trạng thái khóa học không đổi (vẫn đang chờ duyệt) nhưng NGƯỜI chịu trách
        // nhiệm bước này thì đổi — không ghi lại thì về sau không ai truy được vì sao bước lại do người khác duyệt.
        var nameMap = await GetUserNameMapAsync(new int?[] { previousUserId, request.UserId });
        var newName = nameMap.GetValueOrDefault(request.UserId, "người được chọn");
        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
            StepName = step.Name, FromStatus = course.Status, ToStatus = course.Status,
            Reason = previousUserId.HasValue
                ? $"Đổi người duyệt bước \"{step.Name}\": {nameMap.GetValueOrDefault(previousUserId.Value, "—")} → {newName}."
                : $"Chỉ định {newName} duyệt bước \"{step.Name}\"."
        });
        await _db.SaveChangesAsync();

        await _notificationService.NotifyCourseWorkflowAsync(course.Id, new List<int> { request.UserId },
            NotificationType.CourseSubmittedForApproval, "Bạn được chỉ định duyệt nội dung",
            $"Bạn vừa được chỉ định duyệt khóa học \"{course.Title}\" ở bước \"{step.Name}\".");

        return Result.Success(new MessageResponse($"Đã chuyển bước \"{step.Name}\" cho {newName} duyệt."));
    }

    // ---------- LUỒNG CẤP DUYỆT DO ADMIN ĐỊNH NGHĨA ----------
    // Khóa học có chọn luồng (Course.ApprovalWorkflowId) đi qua đúng các bước của luồng đó: mỗi bước giao cho
    // một vai trò ở một phạm vi phòng ban, trạng thái khóa học là PendingApproval suốt chặng đường và bước
    // đang chờ nằm ở Course.CurrentStepOrder. Khóa học KHÔNG chọn luồng vẫn chạy chuỗi cố định
    // Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự ở phần trên của lớp này.

    /// <summary>Một bước của luồng cấp duyệt, rút gọn đủ để trả lời "ai được duyệt bước này".</summary>
    private sealed record WorkflowStepInfo(int OrderIndex, string Name, int RoleId, int? DepartmentId,
        bool AllowDelegate, StepApproverMode ApproverMode, int? FixedUserId);

    /// <summary>Vai trò của người đang đăng nhập, kèm vai trò CHA nếu vai trò đó kế thừa ("Trưởng phòng IT"
    /// kế thừa "Trưởng phòng"). Bước duyệt lưu Id vai trò, còn token chỉ mang TÊN vai trò — nên phải đọc từ
    /// cơ sở dữ liệu thay vì suy từ claim.</summary>
    private sealed record ViewerInfo(int RoleId, int? ParentRoleId);

    private ViewerInfo? _viewer;

    private async Task<ViewerInfo> GetViewerAsync()
    {
        if (_viewer is not null) return _viewer;

        var row = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId)
            .Select(x => new { x.RoleId, x.Role.ParentRoleId })
            .FirstOrDefaultAsync();

        return _viewer = new ViewerInfo(row?.RoleId ?? 0, row?.ParentRoleId);
    }

    /// <summary>Các bước của một luồng, xếp theo thứ tự. Luồng null (khóa học không chọn luồng) trả về rỗng.</summary>
    private async Task<List<WorkflowStepInfo>> GetWorkflowStepListAsync(int? workflowId)
    {
        if (!workflowId.HasValue) return new List<WorkflowStepInfo>();

        return await _db.ApprovalWorkflowStep.AsNoTracking()
            .Where(x => x.ApprovalWorkflowId == workflowId)
            .OrderBy(x => x.OrderIndex)
            .Select(x => new WorkflowStepInfo(x.OrderIndex, x.Name, x.RoleId, x.DepartmentId, x.AllowDelegate,
                x.ApproverMode, x.FixedUserId))
            .ToListAsync();
    }

    /// <summary>Id luồng cấp duyệt hợp lệ để gắn vào khóa học — null nếu người soạn không chọn luồng nào,
    /// HOẶC luồng gửi lên không tồn tại / đã ngừng hoạt động / chưa có bước nào. Bên gọi tự phân biệt hai
    /// trường hợp đó bằng cách xem request có gửi Id hay không.</summary>
    private async Task<int?> ResolveWorkflowIdAsync(int? workflowId)
    {
        if (!workflowId.HasValue) return null;

        // Luồng phân loại theo phòng ban: luồng của phòng khác ghim sẵn người duyệt của phòng đó, chọn phải
        // là bài đi lạc sang cấp trên của một phòng không liên quan. Cho phép thêm luồng DÙNG CHUNG (không
        // gắn phòng ban nào), và Admin thì không chặn — họ dựng luồng cho mọi phòng.
        var myDepartmentId = _currentUser.DepartmentId;
        var valid = await _db.ApprovalWorkflow.AsNoTracking()
            .AnyAsync(x => x.Id == workflowId && x.IsActive && x.StepList.Any()
                        && (_currentUser.IsAdmin || x.DepartmentId == null || x.DepartmentId == myDepartmentId));

        return valid ? workflowId : null;
    }

    /// <summary>Id người duyệt ĐÍCH DANH của một bước, hoặc null nếu bước cứ gửi cho cả nhóm giữ vai trò.
    ///
    /// Bước FixedUser là người mà người dựng luồng đã ghim — nguồn duy nhất của "người duyệt đích danh" kể
    /// từ khi người soạn không còn chỉ định người duyệt cho bài của mình. Các bước còn lại chỉ có người đích
    /// danh khi Admin đã ĐỔI người duyệt giữa chừng (ReassignApprover), lúc đó lấy người vừa đổi sang.
    ///
    /// Không có ai đích danh thì trả về null và bước quay về luật vai trò + phòng ban (gửi cho cả nhóm) — cố
    /// ý như vậy để khóa học vẫn duyệt tiếp được chứ không kẹt lại vì thiếu một dòng dữ liệu.</summary>
    private static int? ResolveDesignatedUserId(WorkflowStepInfo? step, int? assignedUserId) => step is null
        ? null
        : step.ApproverMode == StepApproverMode.FixedUser ? step.FixedUserId : assignedUserId;

    /// <summary>Người đang đăng nhập có được Duyệt / Từ chối bước này không.
    ///
    /// Bước đã chỉ định đích danh người duyệt thì CHÍNH người đó mới bấm được — đây là chỗ khác biệt với bước
    /// gửi cả nhóm: hai Trưởng phòng cùng phòng, chỉ người được người soạn chọn mới duyệt được.
    /// Bước gửi cả nhóm vẫn theo luật cũ: đúng vai trò được giao (vai trò kế thừa cũng tính) VÀ đúng phạm vi
    /// phòng ban của bước (bước không ghi phòng ban nghĩa là cùng phòng ban với người tạo khóa học).
    ///
    /// Người được ủy quyền duyệt (ApprovalDelegate) vẫn duyệt thay được ở bước cho phép ủy quyền, kể cả bước
    /// đã chỉ định đích danh — ủy quyền vốn sinh ra để nội dung không nằm chờ một người đang vắng mặt.</summary>
    private bool CanActOnStep(WorkflowStepInfo step, int? creatorDepartmentId, ViewerInfo viewer, bool isDelegate,
        int? assignedUserId)
    {
        if (step.AllowDelegate && isDelegate) return true;

        var designatedUserId = ResolveDesignatedUserId(step, assignedUserId);
        if (designatedUserId.HasValue) return designatedUserId == _currentUser.UserId;

        if (step.RoleId != viewer.RoleId && step.RoleId != viewer.ParentRoleId) return false;

        var requiredDepartmentId = step.DepartmentId ?? creatorDepartmentId;
        return requiredDepartmentId.HasValue && requiredDepartmentId == _currentUser.DepartmentId;
    }

    /// <summary>Cờ "hiện nút Duyệt / Từ chối" mà giao diện dùng cho khóa học chạy theo luồng — tính sẵn ở
    /// server vì chỉ server mới biết định nghĩa của bước đang chờ.</summary>
    private bool CanApproveWorkflowStep(CourseStatus status, int currentStepOrder, int? creatorDepartmentId,
        IReadOnlyList<WorkflowStepInfo> stepList, ViewerInfo viewer, bool isDelegate, int? assignedUserId)
    {
        if (status != CourseStatus.PendingApproval) return false;

        var step = stepList.FirstOrDefault(x => x.OrderIndex == currentStepOrder);
        return step is not null && CanActOnStep(step, creatorDepartmentId, viewer, isDelegate, assignedUserId);
    }

    /// <summary>Cờ "hiện nút Đổi người duyệt" cho bước đang chờ. Dùng được ở cả bước bắt buộc chỉ định (đổi
    /// sang người khác) lẫn bước gửi cả nhóm (khoanh lại đúng một người khi bài đang nằm chờ mà không ai bấm).
    /// Riêng bước FixedUser thì không: người duyệt do người dựng luồng ghim sẵn, đổi ở màn hình Luồng cấp duyệt.
    /// CHỈ Admin thấy nút này — người soạn không còn chỉ định hay đổi người duyệt cho bài của mình.</summary>
    private bool CanReassignApprover(CourseStatus status, WorkflowStepInfo? currentStep)
        => _currentUser.IsAdmin
        && status == CourseStatus.PendingApproval
        && currentStep is not null
        && currentStep.ApproverMode != StepApproverMode.FixedUser;

    /// <summary>Tên của một loạt người dùng, tra một lần theo danh sách Id (bỏ qua null và trùng lặp).</summary>
    private async Task<Dictionary<int, string>> GetUserNameMapAsync(IReadOnlyCollection<int?> userIdList)
    {
        var idList = userIdList.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, string>();

        return await _db.User.AsNoTracking()
            .Where(x => idList.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName);
    }

    /// <summary>Những người ĐỦ ĐIỀU KIỆN duyệt một bước theo định nghĩa của bước: đúng vai trò (kể cả vai trò
    /// kế thừa) trong đúng phạm vi phòng ban, và còn hoạt động. Đây vừa là danh sách nhận thông báo của bước
    /// gửi cả nhóm, vừa là danh sách người soạn được phép chọn ở bước phải chỉ định.</summary>
    private IQueryable<User> EligibleApproverQuery(WorkflowStepInfo step, int? creatorDepartmentId)
    {
        var departmentId = step.DepartmentId ?? creatorDepartmentId;
        if (!departmentId.HasValue) return _db.User.Where(x => false);

        return _db.User.AsNoTracking()
            .Where(x => x.IsActive && x.DepartmentId == departmentId
                     && (x.RoleId == step.RoleId || x.Role.ParentRoleId == step.RoleId));
    }

    /// <summary>Người nhận thông báo của một bước: người được chỉ định đích danh nếu có, còn không thì cả nhóm
    /// giữ vai trò của bước. Cộng thêm những người được ủy quyền duyệt nếu bước cho phép ủy quyền.
    ///
    /// Đây chính là chỗ khắc phục "bấm gửi duyệt là báo cho tất cả cấp trên": bước đã chỉ định người thì chỉ
    /// đúng người đó nhận được thông báo.</summary>
    private async Task<List<int>> GetStepApproverUserIdListAsync(WorkflowStepInfo step, int? creatorDepartmentId,
        int? assignedUserId)
    {
        var designatedUserId = ResolveDesignatedUserId(step, assignedUserId);

        var idList = designatedUserId.HasValue
            ? new List<int> { designatedUserId.Value }
            : await EligibleApproverQuery(step, creatorDepartmentId).Select(x => x.Id).ToListAsync();

        if (step.AllowDelegate)
        {
            var delegateIdList = await _db.ApprovalDelegate.AsNoTracking().Select(x => x.UserId).ToListAsync();
            idList = idList.Union(delegateIdList).ToList();
        }

        return idList;
    }

    private async Task NotifyStepApproverAsync(Course course, WorkflowStepInfo step,
        NotificationType type, string title, string message, int? assignedUserId)
    {
        var recipientIdList = await GetStepApproverUserIdListAsync(step, course.CreatedBy.DepartmentId, assignedUserId);
        if (recipientIdList.Count == 0) return;

        await _notificationService.NotifyCourseWorkflowAsync(course.Id, recipientIdList, type, title, message);
    }

    /// <summary>Tên người duyệt đích danh của một bước, hoặc null nếu bước gửi cho cả nhóm — dùng để câu thông
    /// báo trả về nói rõ "đang chờ ai" thay vì chỉ nói tên bước.</summary>
    private async Task<string?> ResolveDesignatedNameAsync(WorkflowStepInfo step, int? assignedUserId)
    {
        var designatedUserId = ResolveDesignatedUserId(step, assignedUserId);
        if (!designatedUserId.HasValue) return null;

        return await _db.User.AsNoTracking()
            .Where(x => x.Id == designatedUserId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync();
    }

    /// <summary>Xóa toàn bộ chỉ định người duyệt của một khóa học (bên gọi tự SaveChanges).
    ///
    /// Gọi khi khóa học đổi luồng cấp duyệt hoặc bị đưa về Bản nháp: chỉ định lưu theo SỐ THỨ TỰ bước, mà
    /// bước 2 của luồng này với bước 2 của luồng kia là hai cấp duyệt khác hẳn nhau. Giữ lại là chỉ định cũ
    /// lặng lẽ áp sang bước mới.</summary>
    private void ClearStepApprover(int courseId)
        => _db.CourseStepApprover.RemoveRange(_db.CourseStepApprover.Where(x => x.CourseId == courseId));

    /// <summary>Người được chỉ định duyệt một bước, hoặc null nếu bước đó chưa chỉ định ai. Tra thẳng bằng
    /// GetValueOrDefault sẽ trả về 0 cho bước chưa chỉ định — mà 0 lại lọt qua mọi phép so "đã có người".</summary>
    private static int? AssignedUserIdOf(IReadOnlyDictionary<int, int> assignmentMap, int stepOrder)
        => assignmentMap.TryGetValue(stepOrder, out var userId) ? userId : null;

    /// <summary>Người soạn đã chỉ định ai duyệt bước nào của khóa học này — khóa là số thứ tự bước.</summary>
    private async Task<Dictionary<int, int>> GetAssignedApproverMapAsync(int courseId)
        => await _db.CourseStepApprover.AsNoTracking()
            .Where(x => x.CourseId == courseId)
            .ToDictionaryAsync(x => x.StepOrder, x => x.ApproverUserId);

    /// <summary>Duyệt bước đang chờ của một khóa học chạy theo luồng: còn bước sau thì chuyển tiếp, hết bước
    /// thì xuất bản. Gọi từ <see cref="Approve"/>.</summary>
    private async Task<Result> ApproveWorkflowStepAsync(Course course)
    {
        var stepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
        var currentStep = stepList.FirstOrDefault(x => x.OrderIndex == course.CurrentStepOrder);
        if (currentStep is null)
            return Result.Invalid(new MessageResponse(
                "Luồng cấp duyệt của khóa học đã thay đổi nên không xác định được bước đang chờ. Hãy nhờ người soạn gửi duyệt lại."));

        var assignmentMap = await GetAssignedApproverMapAsync(course.Id);
        var isDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId);
        if (!CanActOnStep(currentStep, course.CreatedBy.DepartmentId, await GetViewerAsync(), isDelegate,
                AssignedUserIdOf(assignmentMap, course.CurrentStepOrder)))
            return Result.Forbidden();

        var nextStep = stepList.FirstOrDefault(x => x.OrderIndex > course.CurrentStepOrder);
        if (nextStep is null && !course.LessonList.Any(l => l.IsActive))
            return Result.Invalid(new MessageResponse("Khóa học chưa có bài học nào, không thể xuất bản."));

        var fromStatus = course.Status;
        course.ApproveWorkflowStep(nextStep?.OrderIndex, DateTime.Now);

        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
            StepName = currentStep.Name, FromStatus = fromStatus, ToStatus = course.Status
        });
        await _db.SaveChangesAsync();

        if (nextStep is null)
        {
            var audienceIdList = await GetCourseAudienceUserIdListAsync(course);
            audienceIdList.Add(course.CreatedById);
            await _notificationService.NotifyCourseWorkflowAsync(course.Id, audienceIdList, NotificationType.CoursePublished,
                "Khóa học đã được xuất bản",
                $"Khóa học \"{course.Title}\" đã được duyệt qua tất cả các bước và chính thức xuất bản.");

            return Result.Success(new MessageResponse("Đã duyệt và xuất bản khóa học."));
        }

        var nextAssignedUserId = AssignedUserIdOf(assignmentMap, nextStep.OrderIndex);
        await NotifyStepApproverAsync(course, nextStep, NotificationType.CourseApproved,
            "Có nội dung khóa học chờ duyệt",
            $"Khóa học \"{course.Title}\" đã qua bước \"{currentStep.Name}\", đang chờ bạn duyệt ở bước \"{nextStep.Name}\".",
            nextAssignedUserId);

        var nextApproverName = await ResolveDesignatedNameAsync(nextStep, nextAssignedUserId);
        return Result.Success(new MessageResponse(nextApproverName is null
            ? $"Đã duyệt, chuyển khóa học sang bước \"{nextStep.Name}\"."
            : $"Đã duyệt, chuyển khóa học sang bước \"{nextStep.Name}\" cho {nextApproverName} duyệt."));
    }

    /// <summary>Từ chối bước đang chờ của một khóa học chạy theo luồng. Gọi từ <see cref="Reject"/>.</summary>
    private async Task<Result> RejectWorkflowStepAsync(Course course, string reason)
    {
        var stepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
        var currentStep = stepList.FirstOrDefault(x => x.OrderIndex == course.CurrentStepOrder);
        if (currentStep is null)
            return Result.Invalid(new MessageResponse(
                "Luồng cấp duyệt của khóa học đã thay đổi nên không xác định được bước đang chờ. Hãy nhờ người soạn gửi duyệt lại."));

        var assignedUserId = await _db.CourseStepApprover.AsNoTracking()
            .Where(x => x.CourseId == course.Id && x.StepOrder == course.CurrentStepOrder)
            .Select(x => (int?)x.ApproverUserId)
            .FirstOrDefaultAsync();
        var isDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId);
        if (!CanActOnStep(currentStep, course.CreatedBy.DepartmentId, await GetViewerAsync(), isDelegate, assignedUserId))
            return Result.Forbidden();

        var fromStatus = course.Status;
        // Tên BƯỚC chứ không phải tên vai trò của người bấm: người được ủy quyền duyệt thay không được lộ danh
        // tính ra ngoài. Gửi duyệt lại sẽ đưa về bước 1.
        course.Reject(reason, currentStep.Name, DateTime.Now);

        _db.CourseApprovalHistory.Add(new CourseApprovalHistory
        {
            CourseId = course.Id, ActorUserId = _currentUser.UserId, ActorRoleName = _currentUser.Role,
            StepName = currentStep.Name, FromStatus = fromStatus, ToStatus = course.Status, Reason = reason
        });
        await _db.SaveChangesAsync();

        await _notificationService.NotifyCourseRejectedAsync(course.Id, currentStep.Name, reason);
        if (currentStep.AllowDelegate && isDelegate) await NotifyGranterOfRejectionAsync(course.Id, reason);

        return Result.Success(new MessageResponse("Đã từ chối nội dung, khóa học được chuyển sang trạng thái Từ chối."));
    }

    /// <summary>Báo cho người đã ủy quyền biết chính người mình ủy quyền vừa từ chối khóa học. Người từ chối
    /// không được ủy quyền (hoặc ủy quyền đã bị thu hồi) thì không có ai để báo — im lặng bỏ qua.</summary>
    private async Task NotifyGranterOfRejectionAsync(int courseId, string reason)
    {
        var grant = await _db.ApprovalDelegate.AsNoTracking()
            .Where(x => x.UserId == _currentUser.UserId)
            .Select(x => new { x.GrantedById, ActorName = x.User.FullName })
            .FirstOrDefaultAsync();
        if (grant is null) return;

        await _notificationService.NotifyDelegateRejectedAsync(courseId, grant.GrantedById, grant.ActorName, reason);
    }

    /// <summary>Danh sách Id người dùng có vai trò + phòng ban chỉ định — dùng để xác định người nhận thông báo ở mỗi bước duyệt.</summary>
    private async Task<List<int>> GetUserIdListByRoleAsync(string roleName, int? departmentId)
    {
        if (!departmentId.HasValue) return new List<int>();

        return await _db.User.AsNoTracking()
            .Where(x => x.Role.Name == roleName && x.DepartmentId == departmentId)
            .Select(x => x.Id)
            .ToListAsync();
    }

    /// <summary>Danh sách Id người dùng thuộc đối tượng của khóa học (theo CourseTarget phòng ban/vị trí, hoặc
    /// toàn bộ nếu không giới hạn) — dùng để báo cho học viên khi khóa học được xuất bản.</summary>
    private async Task<List<int>> GetCourseAudienceUserIdListAsync(Course course)
    {
        var targetList = await _db.CourseTarget.AsNoTracking()
            .Where(t => t.CourseId == course.Id)
            .Select(t => new { t.DepartmentId, t.PositionId })
            .ToListAsync();

        var query = _db.User.AsNoTracking().Where(x => x.IsActive && x.Role.Name != RoleName.Admin);
        if (targetList.Count > 0)
        {
            // EF Core không dịch được .Any() gọi trên list trong bộ nhớ khi so sánh với thuộc tính entity
            // ngay trong cùng biểu thức Where — tách trước thành 2 danh sách Id rồi dùng .Contains() (dịch
            // được thành SQL IN) thay vì .Any() lồng nhau.
            var departmentIdList = targetList.Where(t => t.DepartmentId.HasValue).Select(t => t.DepartmentId!.Value).ToList();
            var positionIdList = targetList.Where(t => t.PositionId.HasValue).Select(t => t.PositionId!.Value).ToList();

            query = query.Where(x =>
                (x.DepartmentId.HasValue && departmentIdList.Contains(x.DepartmentId.Value))
                || (x.PositionId.HasValue && positionIdList.Contains(x.PositionId.Value)));
        }

        return await query.Select(x => x.Id).ToListAsync();
    }

    /// <summary>Xác định người dùng hiện tại có được xem khóa học chưa xuất bản này hay không.</summary>
    private async Task<bool> CanViewCourseAsync(Course course)
    {
        if (_currentUser.IsAdmin || course.Status == CourseStatus.Published) return true;

        if (course.Status == CourseStatus.PendingHrApproval || course.Status == CourseStatus.Rejected)
        {
            if (await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId))
                return true;
        }

        // Luồng cấp duyệt: người đang được giao đúng bước hiện tại luôn xem được nội dung để còn duyệt — bước có
        // thể giao cho phòng ban khác hẳn phòng của người tạo, và người duyệt có thể không giữ vai trò nào trong chuỗi
        // duyệt cố định, nên không điều kiện nào bên dưới phủ được.
        if (course.ApprovalWorkflowId.HasValue
            && (course.Status == CourseStatus.PendingApproval || course.Status == CourseStatus.Rejected))
        {
            var stepList = await GetWorkflowStepListAsync(course.ApprovalWorkflowId);
            var step = stepList.FirstOrDefault(x => x.OrderIndex == course.CurrentStepOrder);
            var assignedUserId = await _db.CourseStepApprover.AsNoTracking()
                .Where(x => x.CourseId == course.Id && x.StepOrder == course.CurrentStepOrder)
                .Select(x => (int?)x.ApproverUserId)
                .FirstOrDefaultAsync();
            if (step is not null && CanActOnStep(step, course.CreatedBy.DepartmentId, await GetViewerAsync(),
                    await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId), assignedUserId))
                return true;
        }

        if (_currentUser.IsTrainer || _currentUser.IsInstructor) return course.CreatedById == _currentUser.UserId;

        if (_currentUser.IsHeadOfDepartment || _currentUser.IsDirector)
        {
            if (_currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == course.CreatedBy.DepartmentId) return true;

            if (_currentUser.IsDirector && course.Status == CourseStatus.PendingHrApproval)
            {
                var hrDepartmentId = await GetHrDepartmentIdAsync();
                return _currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == hrDepartmentId;
            }
        }

        return false;
    }

    /// <summary>Id phòng ban Nhân sự (đánh dấu qua cờ IsHrDepartment, không suy ra từ mã phòng ban) — Giám Đốc
    /// của phòng này là người duyệt xuất bản cuối cùng cho mọi phòng ban. Trước đây dò theo Code == "HR" —
    /// sai với phòng ban do Admin tự tạo qua giao diện vì mã được hệ thống tự sinh từ tên tiếng Việt (VD
    /// "Phòng Nhân Sự" -> "NS"), không bao giờ trùng đúng chữ "HR" trừ khi gõ tay đúng y hệt.</summary>
    private async Task<int?> GetHrDepartmentIdAsync() =>
        (await _db.Department.AsNoTracking().FirstOrDefaultAsync(x => x.IsHrDepartment))?.Id;

    /// <summary>Người đang đăng nhập có đúng là Giám Đốc Nhân sự (Giám Đốc của phòng ban đánh dấu IsHrDepartment)
    /// hay không — chỉ người này mới được xem tên thật của người được ủy quyền duyệt thay.</summary>
    private async Task<bool> IsHrDirectorAsync()
    {
        var hrDepartmentId = await GetHrDepartmentIdAsync();
        return _currentUser.IsDirector && _currentUser.DepartmentId.HasValue && _currentUser.DepartmentId == hrDepartmentId;
    }

    /// <summary>Được phép duyệt/từ chối bước cuối (Giám Đốc Nhân sự) — hoặc chính Giám Đốc Nhân sự, hoặc một
    /// người đã được Giám Đốc Nhân sự ủy quyền duyệt thay (xem ApprovalDelegateService). Trả kèm cờ cho biết
    /// người đang thao tác có đúng là Giám Đốc Nhân sự hay không, để ghi đúng nhãn "ai đã duyệt" lên khóa học.</summary>
    private async Task<(bool Allowed, bool IsHrDirector)> GetHrApprovalPermissionAsync()
    {
        if (await IsHrDirectorAsync()) return (true, true);

        var isDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == _currentUser.UserId);
        return (isDelegate, false);
    }

    /// <summary>Nhãn của bước cuối ("Giám Đốc Nhân sự") trên thanh lộ trình duyệt.
    ///
    /// Bước CHƯA ai xử lý: chính Giám Đốc Nhân sự thấy kèm tên người mình đang ủy quyền ("Giám Đốc Nhân sự ·
    /// Nguyễn Văn A") để biết bài đang nằm ở ai; trả null nghĩa là giữ nguyên nhãn mặc định của bước.
    /// Bước ĐÃ xử lý: chỉ Giám Đốc Nhân sự mới được biết người được ủy quyền đã duyệt/từ chối thay là ai —
    /// các vai trò khác luôn chỉ thấy "Giám Đốc Nhân sự", vì với họ bước đó vẫn là bước của Giám Đốc.</summary>
    private static string? ResolveHrApproverLabel(int? hrApproverUserId, bool hrApproverIsDelegate,
        string? hrApproverName, bool viewerIsHrDirector, string? activeDelegateName = null)
    {
        if (!hrApproverUserId.HasValue)
            return viewerIsHrDirector && !string.IsNullOrWhiteSpace(activeDelegateName)
                ? $"{HrStepLabel} · {activeDelegateName}"
                : null;

        if (!hrApproverIsDelegate) return HrStepLabel;
        return viewerIsHrDirector && !string.IsNullOrWhiteSpace(hrApproverName)
            ? $"{HrStepLabel} · {hrApproverName}"
            : HrStepLabel;
    }

    /// <summary>Tên hiển thị "đang chờ ai" của bước đang dừng, dùng cho khóa học chạy theo LUỒNG CẤP DUYỆT.
    ///
    /// Bước cho phép ủy quyền, mà người đang xem chính là Giám Đốc đã ủy quyền, thì hiện tên người được ủy
    /// quyền chứ không phải tên người duyệt được chỉ định: người sẽ thực sự bấm Duyệt giờ là người được ủy
    /// quyền. Vẫn giữ tên người chỉ định thì ủy quyền xong nhìn vào thanh lộ trình vẫn thấy y hệt lúc chưa
    /// ủy quyền — đúng chỗ khiến người dùng tưởng ủy quyền không ăn.
    ///
    /// activeDelegateName chỉ khác null với người đang xem là Giám Đốc Nhân sự (xem GetActiveDelegateNameAsync),
    /// nên các vai trò khác vẫn thấy đúng tên người được chỉ định như trước — không lộ ai đang được ủy quyền.</summary>
    private static string? ResolveCurrentStepApproverName(CourseStatus status, WorkflowStepInfo? currentStep,
        string? designatedName, string? activeDelegateName, string? rejectedByName = null)
    {
        // Bước không cho ủy quyền thì người bấm luôn đúng là người ghi trên thanh — không có gì để đính chính.
        if (currentStep is not { AllowDelegate: true }) return designatedName;

        // Đã bị từ chối: hiện tên người THỰC SỰ bấm Từ chối. Ủy quyền xong mà bài bị trả về thì câu hỏi đầu
        // tiên của người ủy quyền là "ai từ chối" — tên bước ("Giám Đốc Nhân Sự") không trả lời được, mà lịch
        // sử duyệt thì chưa có màn hình nào hiển thị.
        if (status == CourseStatus.Rejected) return rejectedByName ?? designatedName;

        return status == CourseStatus.PendingApproval && !string.IsNullOrWhiteSpace(activeDelegateName)
            ? activeDelegateName
            : designatedName;
    }

    /// <summary>Tên người đã bấm Từ chối gần nhất của từng khóa học, tra một lần cho cả trang danh sách.
    /// Chỉ gọi cho người xem là Giám Đốc Nhân sự — các vai trò khác không được biết ai từ chối thay.</summary>
    private async Task<Dictionary<int, string>> GetRejectedByNameMapAsync(IReadOnlyCollection<int> courseIdList)
    {
        if (courseIdList.Count == 0) return new Dictionary<int, string>();

        var rowList = await _db.CourseApprovalHistory.AsNoTracking()
            .Where(x => courseIdList.Contains(x.CourseId) && x.ToStatus == CourseStatus.Rejected)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.CourseId, Name = x.ActorUser.FullName })
            .ToListAsync();

        // Một khóa học có thể bị từ chối nhiều lần (gửi duyệt lại rồi lại bị trả) — lấy lần gần nhất.
        return rowList.GroupBy(x => x.CourseId).ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>Tên người mà Giám Đốc Nhân sự ĐANG đăng nhập đã ủy quyền duyệt bước cuối (nhiều nhất một
    /// người — xem ApprovalDelegateService), hoặc null nếu chưa ủy quyền cho ai.</summary>
    private async Task<string?> GetActiveDelegateNameAsync()
        => await _db.ApprovalDelegate.AsNoTracking()
            .Where(x => x.GrantedById == _currentUser.UserId)
            .Select(x => x.User.FullName)
            .FirstOrDefaultAsync();

    public async Task<Result> Delete(int id)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == id);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        if (await _db.Enrollment.AnyAsync(x => x.CourseId == id))
            return Result.Invalid(new MessageResponse("Khóa học đã có người học, không thể xóa. Hãy chuyển sang trạng thái Archived."));

        // Gói SCORM: bản xem thử và bước lộ trình trỏ tới gói không tự lan truyền (NoAction) — dọn trước; tệp đã
        // giải nén trên đĩa dọn SAU khi xóa thành công, để lỗi CSDL không để lại khóa học trỏ vào thư mục đã mất.
        var scormKeyList = await _db.ScormPackage.Where(x => x.CourseId == id).Select(x => x.StorageKey).ToListAsync();
        if (scormKeyList.Count > 0)
        {
            _db.ScormRegistration.RemoveRange(_db.ScormRegistration.Where(x => x.ScormPackage.CourseId == id));
            _db.PathItem.RemoveRange(_db.PathItem.Where(x => x.ScormPackage != null && x.ScormPackage.CourseId == id));
        }

        _db.Course.Remove(course);
        await _db.SaveChangesAsync();
        foreach (var key in scormKeyList) _scormStorage.Delete(key);
        return Result.Success(new MessageResponse("Đã xóa khóa học."));
    }

    public async Task<Result> AddTarget(int id, CourseTargetRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == id))
            return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        if (request.DepartmentId is null && request.PositionId is null)
            return Result.Invalid(new MessageResponse("Phải chọn ít nhất phòng ban hoặc vị trí công việc."));

        var duplicated = await _db.CourseTarget.AnyAsync(x => x.CourseId == id
            && x.DepartmentId == request.DepartmentId && x.PositionId == request.PositionId);
        if (duplicated) return Result.Conflict(new MessageResponse("Đối tượng này đã được thêm."));

        _db.CourseTarget.Add(new CourseTarget
        {
            CourseId = id,
            DepartmentId = request.DepartmentId,
            PositionId = request.PositionId
        });
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã thêm đối tượng đào tạo."));
    }

    public async Task<Result> RemoveTarget(int targetId)
    {
        var target = await _db.CourseTarget.FirstOrDefaultAsync(x => x.Id == targetId);
        if (target is null) return Result.NotFound(new MessageResponse("Không tìm thấy đối tượng đào tạo."));

        _db.CourseTarget.Remove(target);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa đối tượng đào tạo."));
    }
}
