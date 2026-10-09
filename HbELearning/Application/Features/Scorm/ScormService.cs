// File: Application/Features/Scorm/ScormService.cs
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HbELearning.Application.Features.Scorm.CoursePackage;
using HbELearning.Application.Features.Scorm.Export;
using HbELearning.Application.Features.Scorm.Manifest;
using HbELearning.Application.Features.Scorm.Model;
using HbELearning.Application.Features.Scorm.Runtime;
using HbELearning.Application.Features.Scorm.Sequencing;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Scorm;

public class ScormService : IScormService
{
    /// <summary>Trần dung lượng tệp .zip tải lên — khớp RequestSizeLimit ở ScormController.</summary>
    public const long MaxPackageByte = 500L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Mỗi (người học, gói) xử lý tuần tự: engine sequencing đọc - sửa - ghi cả khối trạng thái, hai yêu
    /// cầu chạy song song (Commit tự động đè lên lệnh Tiếp) sẽ ghi đè mất phần của nhau.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LockByKey = new();

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IScormPackageStorage _storage;
    private readonly ILearningService _learningService;
    private readonly ICourseService _courseService;
    private readonly ScormCourseExporter _exporter;
    private readonly CourseScormService _coursePackage;
    private readonly ILearningPathService _learningPathService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ScormService> _logger;

    public ScormService(IAppDbContext db, ICurrentUser currentUser, IScormPackageStorage storage,
        ILearningService learningService, ICourseService courseService, ScormCourseExporter exporter,
        CourseScormService coursePackage, ILearningPathService learningPathService,
        IMemoryCache cache, ILogger<ScormService> logger)
    {
        _coursePackage = coursePackage;
        _learningPathService = learningPathService;
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _learningService = learningService;
        _courseService = courseService;
        _exporter = exporter;
        _cache = cache;
        _logger = logger;
    }

    private static Result Invalid(string message) => Result.Invalid(new MessageResponse(message));

    private static bool IsEditable(Course course) => course.Status is CourseStatus.Draft or CourseStatus.Rejected;

    private const string CoursePackageMessage =
        "Đây là gói SCORM của chính khóa học — sửa nội dung bằng cấu trúc khóa học, không sửa trực tiếp gói.";

    private const string NotEditableMessage =
        "Khóa học đã gửi duyệt hoặc đã xuất bản — nội dung bị khóa, không thêm / sửa / xóa gói SCORM được.";

    // ==================== GÓI: XEM ====================

    public async Task<Result> GetByCourse(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();

        // Gói của chính khóa học (Kind = Course) là cấu trúc khóa học — không phải "gói nhập" để liệt kê ở đây.
        var packageList = await _db.ScormPackage.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.Kind == ScormPackageKind.Imported)
            .Include(x => x.CreatedBy)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var result = new List<ScormPackageDto>();
        foreach (var package in packageList) result.Add(await ToDtoAsync(package, includeTree: false));
        return Result.Success(result);
    }

    public async Task<Result> GetById(int packageId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var package = await _db.ScormPackage.AsNoTracking().Include(x => x.CreatedBy).FirstOrDefaultAsync(x => x.Id == packageId);
        if (package is null) return Result.NotFound(new MessageResponse("Không tìm thấy gói SCORM."));
        return Result.Success(await ToDtoAsync(package, includeTree: true));
    }

    private async Task<ScormPackageDto> ToDtoAsync(ScormPackage package, bool includeTree)
    {
        var learnerCount = await _db.ScormRegistration.CountAsync(x => x.ScormPackageId == package.Id && !x.IsPreview);
        var pathItemId = await _db.PathItem.Where(x => x.ScormPackageId == package.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();

        var tree = new List<ScormTreeNodeDto>();
        if (includeTree)
        {
            var definition = LoadTree(package);
            void Walk(ScormActivityDefinition activity, int depth)
            {
                var sequencing = activity.Sequencing;
                tree.Add(new ScormTreeNodeDto(activity.Id, activity.Title, depth, activity.IsLeaf, activity.IsVisible,
                    activity.IsSco, activity.LaunchUrl,
                    sequencing.PreConditionRuleList.Count + sequencing.PostConditionRuleList.Count
                    + sequencing.ExitConditionRuleList.Count + sequencing.RollupRuleList.Count > 0
                    || sequencing.AttemptLimit.HasValue));
                foreach (var child in activity.Children) Walk(child, depth + 1);
            }
            Walk(definition.Root, 0);
        }

        return new ScormPackageDto(package.Id, package.CourseId, package.Title,
            package.Version == ScormVersion.Scorm12 ? "1.2" : "2004", package.SchemaVersion,
            package.LaunchableCount, package.ScoCount, package.FileSize, package.ExtractedSize, package.FileCount,
            package.DurationMinutes, package.OriginalFileName, package.CreatedAt, package.CreatedBy?.FullName ?? "",
            learnerCount, pathItemId, tree);
    }

    // ==================== GÓI: NHẬP (ĐẦU VÀO) ====================

    public async Task<Result> ImportToCourse(int courseId, ScormPackageImportRequest request, FileUpload? file)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (!IsEditable(course)) return Invalid(NotEditableMessage);

        var (package, error) = await ExtractAndParseAsync(file, request.Title, courseId);
        if (error is not null) return error;

        try
        {
            _db.ScormPackage.Add(package!);
            await _db.SaveChangesAsync();

            // Gói nhập ngoài thành một mục (cụm) của cấu trúc khóa học — vào chương được chọn, hoặc cuối khóa học.
            await _learningPathService.AppendResourceAsync(courseId, PathItemType.Scorm, package!.Id, request.ParentItemId, request.IsRequired);
        }
        catch
        {
            _storage.Delete(package!.StorageKey);
            throw;
        }

        return Result.Created(nameof(GetById), new { id = package!.Id },
            new MessageResponse($"Đã nhập gói SCORM {(package.Version == ScormVersion.Scorm12 ? "1.2" : "2004")} \"{package.Title}\" "
                + $"({package.LaunchableCount} mục nội dung).", package.Id));
    }

    public async Task<Result> ImportAsCourse(ScormCourseImportRequest request, FileUpload? file)
    {
        // Đọc gói TRƯỚC khi tạo khóa học: gói hỏng thì dừng luôn, không để lại một khóa học rỗng mà người soạn
        // (vốn không có quyền xóa khóa học) không dọn được.
        var (package, error) = await ExtractAndParseAsync(file, request.Title, 0);
        if (error is not null) return error;

        try
        {
            var createResult = await _courseService.Create(new CourseCreateRequest
            {
                Title = package!.Title,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                PassScore = request.PassScore,
                IsMandatory = request.IsMandatory,
                DeadlineDay = request.DeadlineDay,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                ApprovalWorkflowId = request.ApprovalWorkflowId,
                DepartmentId = request.DepartmentId,
                AllDepartment = request.AllDepartment,
            });
            if (!createResult.IsSuccess || createResult.Value is not MessageResponse { Id: int courseId })
            {
                _storage.Delete(package.StorageKey);
                return createResult;
            }

            package.CourseId = courseId;
            package.DurationMinutes = request.DurationMinutes;
            _db.ScormPackage.Add(package);
            await _db.SaveChangesAsync();

            await _learningPathService.AppendResourceAsync(courseId, PathItemType.Scorm, package.Id);

            return Result.Created(nameof(GetById), new { id = package.Id },
                new MessageResponse($"Đã tạo khóa học \"{package.Title}\" từ gói SCORM.", courseId));
        }
        catch
        {
            _storage.Delete(package!.StorageKey);
            throw;
        }
    }

    private async Task<(ScormPackage? Package, Result? Error)> ExtractAndParseAsync(FileUpload? file, string? titleOverride, int courseId)
    {
        if (file is null || file.Length == 0) return (null, Invalid("Vui lòng chọn tệp gói SCORM (.zip)."));
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return (null, Invalid("Gói SCORM phải là tệp .zip (Package Interchange File)."));
        if (file.Length > MaxPackageByte)
            return (null, Invalid($"Gói vượt quá {MaxPackageByte / 1024 / 1024} MB."));

        ScormExtractResult extract;
        try
        {
            await using var stream = file.OpenReadStream();
            extract = await _storage.ExtractAsync(stream);
        }
        catch (InvalidDataException ex)
        {
            return (null, Invalid(ex.Message));
        }

        try
        {
            var manifest = _storage.ReadText(extract.StorageKey, "imsmanifest.xml")
                ?? throw new ScormPackageException("Không đọc được imsmanifest.xml.");
            var tree = ScormManifestParser.Parse(manifest, path => _storage.FileExists(extract.StorageKey, path));
            NormalizeTree(tree);

            var leafList = tree.Preorder().Where(a => a.IsLeaf && a != tree.Root).ToList();
            var title = string.IsNullOrWhiteSpace(titleOverride) ? tree.Root.Title : titleOverride.Trim();
            var fileName = Path.GetFileName(file.FileName);

            return (new ScormPackage
            {
                CourseId = courseId,
                Title = title.Length > 250 ? title[..250] : title,
                Version = tree.Version,
                SchemaVersion = tree.SchemaVersion,
                ManifestIdentifier = tree.ManifestIdentifier,
                StorageKey = extract.StorageKey,
                OriginalFileName = fileName.Length > 255 ? fileName[..255] : fileName,
                FileSize = file.Length,
                ExtractedSize = extract.ExtractedSize,
                FileCount = extract.FileCount,
                LaunchableCount = leafList.Count,
                ScoCount = leafList.Count(x => x.IsSco),
                ActivityTreeJson = JsonSerializer.Serialize(tree, JsonOptions),
                CreatedById = _currentUser.UserId,
            }, null);
        }
        catch (ScormPackageException ex)
        {
            _storage.Delete(extract.StorageKey);
            return (null, Invalid(ex.Message));
        }
    }

    public async Task<Result> Update(int packageId, ScormPackageUpdateRequest request)
    {
        var package = await _db.ScormPackage.Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == packageId);
        if (package is null) return Result.NotFound(new MessageResponse("Không tìm thấy gói SCORM."));
        if (package.Kind == ScormPackageKind.Course) return Invalid(CoursePackageMessage);
        if (!IsEditable(package.Course)) return Invalid(NotEditableMessage);

        package.Title = request.Title.Trim();
        package.DurationMinutes = request.DurationMinutes;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu thông tin gói."));
    }

    public async Task<Result> Delete(int packageId)
    {
        var package = await _db.ScormPackage.Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == packageId);
        if (package is null) return Result.NotFound(new MessageResponse("Không tìm thấy gói SCORM."));
        if (package.Kind == ScormPackageKind.Course) return Invalid(CoursePackageMessage);
        if (!IsEditable(package.Course)) return Invalid(NotEditableMessage);
        // Học viên học gói ngay trong lượt học khóa học: dữ liệu SCO của gói nằm ở các hoạt động "P{id}-...".
        var activityPrefix = $"P{packageId}-";
        if (await _db.ScormRegistration.AnyAsync(x => x.ScormPackageId == packageId && !x.IsPreview)
            || await _db.ScormRuntime.AnyAsync(x => x.ActivityId.StartsWith(activityPrefix) && !x.ScormRegistration.IsPreview))
            return Invalid("Đã có học viên học gói này — không xóa được (sẽ mất tiến độ của họ).");

        // Bản xem thử + bước lộ trình không tự lan truyền theo (NoAction) — dọn tay trước.
        _db.ScormRegistration.RemoveRange(_db.ScormRegistration.Where(x => x.ScormPackageId == packageId));
        _db.PathItem.RemoveRange(_db.PathItem.Where(x => x.ScormPackageId == packageId));
        _db.ScormPackage.Remove(package);
        await _db.SaveChangesAsync();

        _storage.Delete(package.StorageKey);
        _cache.Remove(TreeCacheKey(package.Id));
        return Result.Success(new MessageResponse("Đã xóa gói SCORM."));
    }

    public async Task<Result> DownloadPackage(int packageId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var package = await _db.ScormPackage.AsNoTracking().FirstOrDefaultAsync(x => x.Id == packageId);
        if (package is null) return Result.NotFound(new MessageResponse("Không tìm thấy gói SCORM."));
        if (package.Kind == ScormPackageKind.Course) return Invalid("Gói của khóa học tải bằng nút \"Xuất gói SCORM\".");

        var stream = await _exporter.ZipOriginalPackageAsync(package);
        var name = string.IsNullOrWhiteSpace(package.OriginalFileName) ? $"scorm-{package.Id}.zip" : package.OriginalFileName;
        return Result.FileStreamDownload(stream, "application/zip", name);
    }

    // ==================== CÂY HOẠT ĐỘNG / TRẠNG THÁI ====================

    private static string TreeCacheKey(int packageId) => CourseScormService.TreeCacheKey(packageId);

    /// <summary>Cây định nghĩa của gói — bất biến, dùng chung giữa mọi yêu cầu nên giữ trong bộ nhớ.</summary>
    private ScormActivityTree LoadTree(ScormPackage package)
        => _cache.GetOrCreate(TreeCacheKey(package.Id), entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(30);
            var tree = JsonSerializer.Deserialize<ScormActivityTree>(package.ActivityTreeJson, JsonOptions)!;
            NormalizeTree(tree);
            return tree;
        })!;

    /// <summary>Mọi hoạt động có đúng một mục tiêu chính (SN 4.2.1.1) — điền sẵn trước khi cây được dùng chung, để
    /// engine không phải sửa cây lúc chạy (nhiều yêu cầu cùng đọc một cây).</summary>
    private static void NormalizeTree(ScormActivityTree tree)
    {
        foreach (var activity in tree.Preorder())
            if (activity.Sequencing.PrimaryObjective is null)
                activity.Sequencing.ObjectiveList.Insert(0, new ObjectiveDefinition { IsPrimary = true });
    }

    private static ScormSequencingState LoadState(string? json)
        => string.IsNullOrEmpty(json)
            ? new ScormSequencingState()
            : JsonSerializer.Deserialize<ScormSequencingState>(json, JsonOptions) ?? new ScormSequencingState();

    private static SemaphoreSlim LockFor(int userId, int packageId)
        => LockByKey.GetOrAdd($"{userId}:{packageId}", _ => new SemaphoreSlim(1, 1));

    // ==================== PHIÊN HỌC ====================

    /// <summary>Engine cho gói đang mở. Gói nhập ngoài (xem thử riêng) chạy đúng engine chuẩn; gói của khóa học thêm
    /// phần nối với kết quả chấm trên máy chủ và mục tiêu khóa tuần tự do LMS giữ (xem CourseScormService).</summary>
    private static ScormSequencer NewSequencer(ScormPackage package, ScormActivityTree tree, ScormSequencingState state, CourseAuthority? authority)
        => package.Kind == ScormPackageKind.Course
            ? CourseScormService.CreateSequencer(tree, state, authority)
            : new ScormSequencer(tree, state);

    /// <summary>Kết quả có thẩm quyền của lượt học — chỉ có với học viên thật trên gói của khóa học.</summary>
    private async Task<CourseAuthority?> LoadAuthorityAsync(ScormPackage package, ScormRegistration registration, ScormActivityTree tree)
        => package.Kind == ScormPackageKind.Course && !registration.IsPreview && registration.EnrollmentId is int enrollmentId
            ? await _coursePackage.LoadAuthorityAsync(enrollmentId, tree)
            : null;

    public async Task<Result> Launch(ScormLaunchRequest request)
    {
        ScormPackage? package;
        Enrollment? enrollment = null;

        if (request.EnrollmentId is int enrollmentId)
        {
            // Học viên: khóa học CHÍNH LÀ một gói SCORM — mở gói của khóa học trong lượt học này.
            enrollment = await _db.Enrollment.FirstOrDefaultAsync(x => x.Id == enrollmentId);
            if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
            if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
            if (enrollment.Status == EnrollmentStatus.Cancelled) return Invalid("Khóa học này đã bị hủy phân công.");
            package = await _coursePackage.EnsureCoursePackageAsync(enrollment.CourseId);
        }
        else if (request.CourseId is int courseId)
        {
            // Người soạn / người duyệt xem thử khóa học đúng như học viên sẽ thấy.
            if (!_currentUser.IsContentViewer) return Result.Forbidden();
            if (!await _db.Course.AnyAsync(x => x.Id == courseId)) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
            package = await _coursePackage.EnsureCoursePackageAsync(courseId);
        }
        else if (request.PackageId is int packageId)
        {
            // Xem thử riêng một gói nhập ngoài.
            if (!_currentUser.IsContentViewer) return Result.Forbidden();
            package = await _db.ScormPackage.FirstOrDefaultAsync(x => x.Id == packageId);
            if (package is null) return Result.NotFound(new MessageResponse("Không tìm thấy gói SCORM."));
        }
        else
        {
            return Invalid("Thiếu khóa học cần mở.");
        }

        var userId = _currentUser.UserId;
        var gate = LockFor(userId, package.Id);
        await gate.WaitAsync();
        try
        {
            var registration = enrollment is not null
                ? await _db.ScormRegistration.FirstOrDefaultAsync(x => x.EnrollmentId == enrollment.Id && x.ScormPackageId == package.Id)
                : await _db.ScormRegistration.FirstOrDefaultAsync(x => x.UserId == userId && x.ScormPackageId == package.Id && x.IsPreview);
            if (registration is null)
            {
                registration = new ScormRegistration
                {
                    EnrollmentId = enrollment?.Id,
                    UserId = userId,
                    ScormPackageId = package.Id,
                    IsPreview = enrollment is null,
                };
                _db.ScormRegistration.Add(registration);
                await _db.SaveChangesAsync();
            }

            var tree = LoadTree(package);
            var authority = await LoadAuthorityAsync(package, registration, tree);
            var sequencer = NewSequencer(package, tree, LoadState(registration.StateJson), authority);
            registration.SessionId = Guid.NewGuid().ToString("N");
            ScormSequencingResult? result = null;

            if (request.Restart)
            {
                sequencer.ResetForNewAttempt();
                // Lượt mới xóa mục tiêu toàn cục của lượt cũ — dựng lại engine để kết quả có thẩm quyền và mục tiêu khóa
                // tuần tự được nạp lại trước khi Start xét điều kiện các mục.
                sequencer = NewSequencer(package, tree, sequencer.State, authority);
                // SCORM 1.2 không có khái niệm lượt thử: dữ liệu của SCO sống qua các lần mở. "Học lại từ đầu" là
                // lúc duy nhất xóa nó đi.
                if (package.Version == ScormVersion.Scorm12)
                    _db.ScormRuntime.RemoveRange(_db.ScormRuntime.Where(x => x.ScormRegistrationId == registration.Id));
                else
                {
                    var legacyIdList = tree.Preorder().Where(a => a.RuntimeVersion == "1.2").Select(a => a.Id).ToList();
                    if (legacyIdList.Count > 0)
                        _db.ScormRuntime.RemoveRange(_db.ScormRuntime.Where(x => x.ScormRegistrationId == registration.Id && legacyIdList.Contains(x.ActivityId)));
                }
            }

            // Phiên trước bị bỏ ngang (đóng trình duyệt mà lệnh tạm dừng không kịp tới máy chủ): coi như học viên đã
            // bấm "Lưu và thoát" để lần này học tiếp đúng chỗ, thay vì kẹt lại vì Start chỉ hợp lệ khi chưa có
            // hoạt động hiện tại.
            if (sequencer.HasCurrentActivity)
            {
                if (sequencer.IsCurrentActivityActive)
                {
                    sequencer.Navigate(new ScormNavigationRequest("suspendAll"));
                }
                else
                {
                    // Nội dung đã tự kết thúc ở phiên trước (học viên đóng trang lúc đang chọn mục tiếp theo): đi TIẾP
                    // từ chỗ đó. Start sẽ mở lại mục đầu tiên — một lượt thử mới xóa trạng thái hoàn thành của nó.
                    var snapshot = JsonSerializer.Serialize(sequencer.State, JsonOptions);
                    var next = sequencer.Navigate(new ScormNavigationRequest("continue"));
                    if (next.DeliveredActivityId is not null) result = next;
                    else
                    {
                        sequencer = NewSequencer(package, tree, LoadState(snapshot), authority);
                        sequencer.State.CurrentActivityId = null;
                    }
                }
            }

            // Mở thẳng một mục (học viên bấm một mục cụ thể ở trang khóa học, hoặc "Quay lại" từ trang lịch sử làm
            // bài). Chuẩn không cho chọn mục đó thì thôi, học tiếp như thường.
            if (result is null && !string.IsNullOrWhiteSpace(request.TargetActivityId)
                && sequencer.FindActivity(request.TargetActivityId) is not null)
            {
                var snapshot = JsonSerializer.Serialize(sequencer.State, JsonOptions);
                var chosen = sequencer.Navigate(new ScormNavigationRequest("choice", request.TargetActivityId));
                if (chosen.DeliveredActivityId is not null) result = chosen;
                else sequencer = NewSequencer(package, tree, LoadState(snapshot), authority);
            }

            // Khóa học đã học dở mà không có điểm tạm dừng (học ở màn hình cũ, hoặc lần trước bấm Kết thúc): Start của
            // chuẩn luôn giao mục ĐẦU TIÊN của cây — bắt học viên lật lại bài đã xong. LMS được quyền chọn thay (choice):
            // mở mục đầu tiên chưa xong mà sequencing cho chọn.
            if (result is null && package.Kind == ScormPackageKind.Course && !sequencer.HasSuspendedActivity)
            {
                var availability = sequencer.EvaluateAvailability();
                var statusList = sequencer.ActivityStatusList(availability);
                var firstOpen = statusList.FirstOrDefault(a => a.IsLeaf && a.IsVisible && a.CanChoose
                                                              && !(a.Completion == "completed" && a.Success != "failed"));
                var firstLeaf = statusList.FirstOrDefault(a => a.IsLeaf && a.Depth > 0);
                if (firstOpen is not null && firstOpen.Id != firstLeaf?.Id)
                {
                    var snapshot = JsonSerializer.Serialize(sequencer.State, JsonOptions);
                    var chosen = sequencer.Navigate(new ScormNavigationRequest("choice", firstOpen.Id));
                    if (chosen.DeliveredActivityId is not null) result = chosen;
                    else sequencer = NewSequencer(package, tree, LoadState(snapshot), authority);
                }
            }

            result ??= sequencer.HasSuspendedActivity
                ? sequencer.Navigate(new ScormNavigationRequest("resumeAll"))
                : sequencer.Navigate(new ScormNavigationRequest("start"));

            // Hoạt động đang tạm dừng là một cụm (không giao được): đưa học viên về mục học gần nhất nếu chuẩn cho chọn
            // mục đó, không thì học tiếp từ đầu cây — tiến độ đã có vẫn giữ nguyên.
            if (result.DeliveredActivityId is null && !result.EndSequencingSession && sequencer.HasSuspendedActivity)
            {
                sequencer.State.SuspendedActivityId = null;
                if (sequencer.State.LastDeliveredActivityId is string lastId)
                    result = sequencer.Navigate(new ScormNavigationRequest("choice", lastId));
                if (result.DeliveredActivityId is null)
                    result = sequencer.Navigate(new ScormNavigationRequest("start"));
            }

            // Start không giao được gì (gói 2004 không bật flow ở gốc — mặc định của chuẩn — hoặc mục đầu tiên chưa tới
            // lịch mở): chuẩn để học viên tự chọn trong mục lục. Mở sẵn mục đầu tiên chọn được để người học không đứng
            // trước một màn hình trống.
            if (result.DeliveredActivityId is null && !result.EndSequencingSession)
            {
                var availability = sequencer.EvaluateAvailability();
                var first = tree.Preorder().FirstOrDefault(a => a.IsLeaf && a != tree.Root && a.IsVisible
                                                               && availability.ChoiceValidIdSet.Contains(a.Id));
                if (first is not null) result = sequencer.Navigate(new ScormNavigationRequest("choice", first.Id));
            }

            var message = result.EndSequencingSession
                ? "Không còn nội dung nào để học trong khóa học này."
                : result.DeliveredActivityId is null ? ScormMessages.FromException(result.ExceptionCode) ?? "Hãy chọn một mục trong mục lục để bắt đầu." : null;

            return Result.Success(await FinishAsync(registration, package, tree, sequencer, authority, result, message));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result> Commit(int registrationId, ScormCommitRequest request)
    {
        var registration = await _db.ScormRegistration.Include(x => x.ScormPackage).FirstOrDefaultAsync(x => x.Id == registrationId);
        if (registration is null) return Result.NotFound(new MessageResponse("Không tìm thấy phiên học."));
        if (registration.UserId != _currentUser.UserId) return Result.Forbidden();

        var gate = LockFor(registration.UserId, registration.ScormPackageId);
        await gate.WaitAsync();
        try
        {
            await _db.Entry(registration).ReloadAsync();
            if (registration.SessionId != request.SessionId) return SessionConflict();

            var package = registration.ScormPackage;
            var tree = LoadTree(package);
            var authority = await LoadAuthorityAsync(package, registration, tree);
            var sequencer = NewSequencer(package, tree, LoadState(registration.StateJson), authority);
            if (sequencer.State.CurrentActivityId != request.ActivityId)
                return Result.Conflict(new MessageResponse("Nội dung này không còn là nội dung đang mở."));

            var before = OutcomeKey(registration);
            // Thời lượng phiên chỉ cộng một lần, lúc SCO kết thúc — Commit giữa chừng không cộng.
            await SaveRuntimeAsync(registration, request.ActivityId, request.Runtime, isTermination: false);
            request.Runtime.Report.SessionTimeSecond = 0;
            sequencer.ApplyRuntimeReport(request.ActivityId, request.Runtime.Report);
            // SCO hệ thống chỉ là người đưa tin: kết quả chấm trên máy chủ đè lên mọi thứ SCO vừa báo.
            if (authority is not null) CourseScormService.ApplyAuthority(sequencer, authority);

            UpdateSummary(registration, sequencer);
            registration.StateJson = JsonSerializer.Serialize(sequencer.State, JsonOptions);
            registration.LastAccessedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            await RecalculateIfChangedAsync(registration, before);

            return Result.Success(new ScormCommitResultDto(BuildNavState(sequencer), ToSummary(registration)));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result> Navigate(int registrationId, ScormNavigateRequest request)
    {
        var registration = await _db.ScormRegistration.Include(x => x.ScormPackage).ThenInclude(p => p.Course)
            .FirstOrDefaultAsync(x => x.Id == registrationId);
        if (registration is null) return Result.NotFound(new MessageResponse("Không tìm thấy phiên học."));
        if (registration.UserId != _currentUser.UserId) return Result.Forbidden();

        var type = string.IsNullOrWhiteSpace(request.Request) || request.Request == "_none_" ? null : request.Request.Trim();
        if (type is not null && !ScormNavigationRequest.TypeSet.Contains(type)) return Invalid("Yêu cầu điều hướng không hợp lệ.");

        var gate = LockFor(registration.UserId, registration.ScormPackageId);
        await gate.WaitAsync();
        try
        {
            await _db.Entry(registration).ReloadAsync();
            if (registration.SessionId != request.SessionId) return SessionConflict();

            var package = registration.ScormPackage;
            var tree = LoadTree(package);
            var authority = await LoadAuthorityAsync(package, registration, tree);
            var sequencer = NewSequencer(package, tree, LoadState(registration.StateJson), authority);
            var before = OutcomeKey(registration);

            // ----- SCO vừa kết thúc: chép dữ liệu cuối cùng của nó vào mô hình theo dõi -----
            string? exit = null;
            if (request.ActivityId is not null && request.Runtime is not null
                && sequencer.State.CurrentActivityId == request.ActivityId && sequencer.IsCurrentActivityActive)
            {
                await SaveRuntimeAsync(registration, request.ActivityId, request.Runtime, isTermination: true);
                sequencer.ApplyRuntimeReport(request.ActivityId, request.Runtime.Report);
                exit = request.Runtime.Report.Exit;
                if (exit == "suspend") sequencer.MarkSuspendedByContent(request.ActivityId);
            }
            // Kết quả chấm trên máy chủ phải có mặt TRƯỚC khi sequencing xét điều kiện mục kế tiếp (VD vừa nộp bài
            // kiểm tra đạt thì "Tiếp" phải mở được mục sau ngay trong yêu cầu này).
            if (authority is not null) CourseScormService.ApplyAuthority(sequencer, authority);

            // SCO kết thúc mà không xin điều hướng gì: kết thúc lượt thử của nó (Exit) rồi chờ học viên chọn tiếp —
            // nếu có luật hậu điều kiện (continue, retry...) thì chính bước Exit này kích hoạt chúng.
            if (type is null && sequencer.IsCurrentActivityActive) type = "exit";
            // cmi.exit = "time-out" / "logout": chuẩn RTE coi như học viên thoát toàn bộ.
            if (exit is "time-out" or "logout" && type is "exit") type = "exitAll";

            var result = type is null ? new ScormSequencingResult() : sequencer.Navigate(new ScormNavigationRequest(type, request.TargetActivityId));

            string? message = null;
            if (result.EndSequencingSession)
                message = type == "suspendAll" ? "Đã lưu chỗ đang học. Lần sau mở lại sẽ học tiếp đúng chỗ này." : "Đã kết thúc phiên học.";
            else if (result.DeliveredActivityId is null)
                message = ScormMessages.FromException(result.ExceptionCode)
                          ?? (sequencer.HasCurrentActivity ? "Nội dung đã kết thúc. Chọn mục tiếp theo trong mục lục hoặc bấm Tiếp." : null);

            return Result.Success(await FinishAsync(registration, package, tree, sequencer, authority, result, message, before));
        }
        finally
        {
            gate.Release();
        }
    }

    private static Result SessionConflict() => Result.Conflict(new MessageResponse(
        "Khóa học này vừa được mở ở một cửa sổ khác — phiên học ở cửa sổ này đã dừng để không ghi đè tiến độ. Hãy dùng cửa sổ mới nhất."));

    // ----- Giao một hoạt động + chốt trạng thái -----

    private async Task<ScormSessionDto> FinishAsync(ScormRegistration registration, ScormPackage package,
        ScormActivityTree tree, ScormSequencer sequencer, CourseAuthority? authority, ScormSequencingResult result, string? message,
        string? before = null)
    {
        before ??= OutcomeKey(registration);

        ScormDeliveryDto? delivery = null;
        if (result.DeliveredActivityId is string deliveredId && sequencer.FindActivity(deliveredId) is ScormActivityDefinition activity)
            delivery = await BuildDeliveryAsync(registration, package, activity, sequencer, result.IsResumedDelivery);
        // Lượt thử mới vừa mở trên SCO hệ thống xóa trạng thái của lượt cũ (đúng chuẩn) — nạp lại kết quả đã chấm để mục
        // lục không tụt "chưa học" một bài đã đạt chỉ vì học viên mở ra xem lại.
        if (authority is not null) CourseScormService.ApplyAuthority(sequencer, authority);

        UpdateSummary(registration, sequencer);
        registration.StateJson = JsonSerializer.Serialize(sequencer.State, JsonOptions);
        registration.LastAccessedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await RecalculateIfChangedAsync(registration, before);

        var course = package.Course ?? await _db.Course.AsNoTracking().FirstAsync(x => x.Id == package.CourseId);
        return new ScormSessionDto(registration.Id, registration.SessionId!, package.Id,
            package.Kind == ScormPackageKind.Course ? course.Title : package.Title,
            package.Version == ScormVersion.Scorm12 ? "1.2" : "2004",
            registration.EnrollmentId, registration.IsPreview, course.Id, course.Title,
            delivery, result.EndSequencingSession, message, BuildNavState(sequencer), ToSummary(registration),
            package.Kind == ScormPackageKind.Course);
    }

    private async Task<ScormDeliveryDto> BuildDeliveryAsync(ScormRegistration registration, ScormPackage package,
        ScormActivityDefinition activity, ScormSequencer sequencer, bool resumed)
    {
        var runtime = await _db.ScormRuntime.FirstOrDefaultAsync(x => x.ScormRegistrationId == registration.Id && x.ActivityId == activity.Id);
        if (runtime is null)
        {
            runtime = new ScormRuntime { ScormRegistrationId = registration.Id, ActivityId = activity.Id };
            _db.ScormRuntime.Add(runtime);
        }

        // Một khóa học có thể nhúng gói 1.2 bên cạnh các SCO 2004: phiên bản bộ API là của TỪNG hoạt động.
        var runtimeVersion = activity.RuntimeVersion ?? (package.Version == ScormVersion.Scorm12 ? "1.2" : "2004");
        string entry;
        if (runtimeVersion == "2004")
        {
            // 2004: lượt thử mới trên SCO = mô hình dữ liệu mới tinh (cmi.entry = ab-initio); chỉ học tiếp một lượt
            // đang tạm dừng mới đọc lại dữ liệu cũ (resume).
            if (resumed && runtime.CmiJson is not null) entry = "resume";
            else
            {
                ResetRuntime(runtime);
                entry = "ab-initio";
            }
        }
        else
        {
            // 1.2: dữ liệu sống qua mọi lần mở; "resume" khi lần trước thoát bằng suspend.
            entry = runtime.CmiJson is null ? "ab-initio" : resumed ? "resume" : "";
        }

        runtime.SessionCount++;
        runtime.FirstAccessedAt ??= DateTime.Now;
        runtime.UpdatedAt = DateTime.Now;

        var learner = await _db.User.AsNoTracking().Where(x => x.Id == registration.UserId)
            .Select(x => new { x.EmployeeCode, x.FullName }).FirstAsync();
        var primary = activity.Sequencing.PrimaryObjective;

        var init = new ScormCmiInitDto(
            LearnerId: string.IsNullOrWhiteSpace(learner.EmployeeCode) ? registration.UserId.ToString(CultureInfo.InvariantCulture) : learner.EmployeeCode,
            LearnerName: learner.FullName,
            Credit: registration.IsPreview ? "no-credit" : "credit",
            Mode: "normal",
            Entry: entry,
            LaunchData: activity.DataFromLms,
            CompletionThreshold: activity.CompletionThreshold is { CompletedByMeasure: true } threshold ? threshold.MinProgressMeasure : null,
            ScaledPassingScore: primary is { SatisfiedByMeasure: true } ? primary.MinNormalizedMeasure : null,
            MaxTimeAllowed: activity.MaxTimeAllowed,
            TimeLimitAction: activity.TimeLimitAction,
            MasteryScore: activity.MasteryScore,
            TotalTimeSecond: runtime.TotalTimeSecond,
            ObjectiveList: sequencer.InitialObjectiveList(activity.Id),
            SharedDataList: sequencer.SharedDataFor(activity.Id).Select(x => new ScormSharedDataDto(x.Id, x.Store, x.Readable, x.Writable)).ToList());

        var launchUrl = ResolveLaunchUrl(package, activity.LaunchUrl!);
        if (CourseScormService.IsInternalLaunch(launchUrl))
            launchUrl = CourseScormService.WithContext(launchUrl, registration.IsPreview ? null : registration.EnrollmentId, package.CourseId);

        return new ScormDeliveryDto(activity.Id, activity.Title, launchUrl, activity.IsSco,
            activity.HideLmsUi, entry == "ab-initio" && runtimeVersion == "2004" ? null : runtime.CmiJson, init, runtimeVersion);
    }

    /// <summary>Địa chỉ tuyệt đối (http, hoặc bắt đầu bằng "/" — SCO hệ thống và gói nhúng trong cây khóa học) giữ
    /// nguyên; địa chỉ tương đối là tệp trong kho của chính gói.</summary>
    private static string ResolveLaunchUrl(ScormPackage package, string launchUrl)
        => launchUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || launchUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || launchUrl.StartsWith('/')
            ? launchUrl
            : $"/scorm-content/{package.StorageKey}/{launchUrl}";

    private static void ResetRuntime(ScormRuntime runtime)
    {
        runtime.CmiJson = null;
        runtime.CompletionStatus = "unknown";
        runtime.SuccessStatus = "unknown";
        runtime.ScoreScaled = runtime.ScoreRaw = runtime.ScoreMin = runtime.ScoreMax = runtime.ProgressMeasure = null;
        runtime.TotalTimeSecond = 0;
        runtime.SessionCount = 0;
        runtime.InteractionCount = 0;
    }

    private async Task SaveRuntimeAsync(ScormRegistration registration, string activityId, ScormRuntimePayload payload, bool isTermination)
    {
        var runtime = await _db.ScormRuntime.FirstOrDefaultAsync(x => x.ScormRegistrationId == registration.Id && x.ActivityId == activityId);
        if (runtime is null)
        {
            runtime = new ScormRuntime { ScormRegistrationId = registration.Id, ActivityId = activityId };
            _db.ScormRuntime.Add(runtime);
        }

        var report = payload.Report;
        if (payload.CmiJson is not null) runtime.CmiJson = payload.CmiJson;
        runtime.CompletionStatus = Vocabulary(report.CompletionStatus, "completed", "incomplete", "not attempted", "unknown");
        runtime.SuccessStatus = Vocabulary(report.SuccessStatus, "passed", "failed", "unknown");
        runtime.ScoreScaled = ToDecimal(report.ScoreScaled, -1, 1);
        runtime.ScoreRaw = ToDecimal(report.ScoreRaw);
        runtime.ScoreMin = ToDecimal(report.ScoreMin);
        runtime.ScoreMax = ToDecimal(report.ScoreMax);
        runtime.ProgressMeasure = ToDecimal(report.ProgressMeasure, 0, 1);
        runtime.InteractionCount = Math.Max(0, payload.InteractionCount);
        runtime.UpdatedAt = DateTime.Now;

        if (isTermination)
        {
            // Một phiên dài hơn một ngày là số liệu hỏng (máy ngủ, đồng hồ lệch) — kẹp lại thay vì cộng bừa.
            var second = (int)Math.Clamp(report.SessionTimeSecond, 0, 86_400);
            runtime.TotalTimeSecond += second;
            registration.TotalTimeSecond += second;
        }

        static string Vocabulary(string? value, params string[] allowed)
            => value is not null && allowed.Contains(value) ? value : allowed[^1];
    }

    private static decimal? ToDecimal(double? value, double min = -1e12, double max = 1e12)
        => value is double number && double.IsFinite(number)
            ? Math.Round((decimal)Math.Clamp(number, min, max), 6)
            : null;

    private static void UpdateSummary(ScormRegistration registration, ScormSequencer sequencer)
    {
        var (completion, success, scaled) = sequencer.RootSummary();
        registration.CompletionStatus = completion;
        registration.SuccessStatus = success;
        registration.ScoreScaled = ToDecimal(scaled, -1, 1);
        if (registration.ScoreScaled is decimal score && (registration.BestScoreScaled is null || score > registration.BestScoreScaled))
            registration.BestScoreScaled = score;
        registration.IsSuspended = sequencer.HasSuspendedActivity;
        registration.AttemptCount = sequencer.RootAttemptCount;

        if (completion == "completed" && success != "failed" && registration.CompletedAt is null)
            registration.CompletedAt = DateTime.Now;
    }

    /// <summary>Tóm tắt gốc + kết quả từng gói nhập ngoài trong lượt học — đổi thì tiến độ khóa học phải tính lại.</summary>
    private static string OutcomeKey(ScormRegistration registration)
        => $"{registration.CompletionStatus}|{registration.SuccessStatus}|{registration.ScoreScaled}|{registration.CompletedAt}"
           + "#" + CourseScormService.PackageOutcomeKey(LoadState(registration.StateJson));

    private async Task RecalculateIfChangedAsync(ScormRegistration registration, string before)
    {
        if (registration.IsPreview || registration.EnrollmentId is not int enrollmentId) return;
        if (OutcomeKey(registration) == before) return;
        await _learningService.RecalculateAsync(enrollmentId);
    }

    private static ScormSummaryDto ToSummary(ScormRegistration registration) => new(
        registration.CompletionStatus, registration.SuccessStatus,
        registration.ScoreScaled is decimal scaled ? Math.Round(scaled * 100, 2) : null,
        registration.TotalTimeSecond, registration.AttemptCount, registration.IsSuspended, registration.CompletedAt);

    private static ScormNavStateDto BuildNavState(ScormSequencer sequencer)
    {
        var availability = sequencer.EvaluateAvailability();
        return new ScormNavStateDto(availability.CanContinue, availability.CanPrevious, sequencer.HasCurrentActivity,
            sequencer.ActivityStatusList(availability),
            availability.ChoiceValidIdSet.ToList(), availability.JumpValidIdSet.ToList());
    }

    // ==================== BÁO CÁO (ĐẦU RA) ====================

    public async Task<Result> GetReport(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        return Result.Success(await QueryReportAsync(courseId, null));
    }

    private async Task<List<ScormReportRowDto>> QueryReportAsync(int courseId, int? registrationId)
    {
        var query = _db.ScormRegistration.AsNoTracking()
            .Where(x => !x.IsPreview && x.EnrollmentId != null && x.ScormPackage.CourseId == courseId);
        if (registrationId.HasValue) query = query.Where(x => x.Id == registrationId);

        return await query
            .OrderBy(x => x.User.FullName).ThenBy(x => x.ScormPackageId)
            .Select(x => new ScormReportRowDto(
                x.Id, x.EnrollmentId!.Value, x.UserId, x.User.EmployeeCode, x.User.FullName,
                x.User.Department != null ? x.User.Department.Name : null,
                x.ScormPackageId, x.ScormPackage.Title,
                x.CompletionStatus, x.SuccessStatus,
                x.ScoreScaled != null ? x.ScoreScaled * 100 : null,
                x.BestScoreScaled != null ? x.BestScoreScaled * 100 : null,
                x.TotalTimeSecond, x.AttemptCount, x.IsSuspended, x.LastAccessedAt, x.CompletedAt))
            .ToListAsync();
    }

    public async Task<Result> GetRegistrationDetail(int registrationId)
    {
        var registration = await _db.ScormRegistration.AsNoTracking().Include(x => x.ScormPackage)
            .FirstOrDefaultAsync(x => x.Id == registrationId);
        if (registration is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi học."));
        if (!_currentUser.IsContentViewer && registration.UserId != _currentUser.UserId) return Result.Forbidden();

        var row = (await QueryReportAsync(registration.ScormPackage.CourseId, registrationId)).FirstOrDefault();
        if (row is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi học."));

        var tree = LoadTree(registration.ScormPackage);
        var state = LoadState(registration.StateJson);
        var runtimeById = await _db.ScormRuntime.AsNoTracking()
            .Where(x => x.ScormRegistrationId == registrationId)
            .ToDictionaryAsync(x => x.ActivityId);

        // Hoàn thành / đạt / điểm lấy từ MÔ HÌNH THEO DÕI (sau rollup và — với khóa học — sau khi đối chiếu kết quả chấm
        // trên máy chủ), không lấy từ lần SCO báo gần nhất: SCO mở ra rồi bị đóng ngang chưa kịp báo gì vẫn là một
        // mục đã học xong.
        var summaryById = new ScormSequencer(tree, state).ActivitySummaryMap();

        var activityList = new List<ScormActivityReportDto>();
        foreach (var activity in tree.Preorder().Where(a => a.IsLeaf && a != tree.Root))
        {
            runtimeById.TryGetValue(activity.Id, out var runtime);
            summaryById.TryGetValue(activity.Id, out var summary);
            var cmi = ParseCmi(runtime?.CmiJson);
            activityList.Add(new ScormActivityReportDto(
                activity.Id, activity.Title, activity.IsSco,
                summary?.Completed switch { true => "completed", false => "incomplete", _ => runtime?.CompletionStatus ?? "unknown" },
                summary?.Satisfied switch { true => "passed", false => "failed", _ => runtime?.SuccessStatus ?? "unknown" },
                summary?.Measure is double measure ? ToDecimal(measure, -1, 1) : runtime?.ScoreScaled,
                runtime?.ScoreRaw, runtime?.ScoreMin, runtime?.ScoreMax, runtime?.ProgressMeasure,
                runtime?.TotalTimeSecond ?? 0, runtime?.SessionCount ?? 0, summary?.AttemptCount ?? 0, runtime?.UpdatedAt,
                Get(cmi, "cmi.location") ?? Get(cmi, "cmi.core.lesson_location"),
                ReadInteractionList(cmi)));
        }

        return Result.Success(new ScormRegistrationDetailDto(row, activityList));
    }

    public async Task<Result> ExportReportCsv(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var rowList = await QueryReportAsync(courseId, null);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", "Mã NV", "Họ tên", "Phòng ban", "Gói SCORM", "Hoàn thành", "Kết quả",
            "Điểm (%)", "Điểm cao nhất (%)", "Thời gian học (phút)", "Số lượt", "Đang tạm dừng", "Truy cập gần nhất", "Ngày hoàn thành"));
        foreach (var row in rowList)
            csv.AppendLine(string.Join(",",
                Csv(row.EmployeeCode), Csv(row.FullName), Csv(row.DepartmentName), Csv(row.PackageTitle),
                Csv(CompletionLabel(row.CompletionStatus)), Csv(SuccessLabel(row.SuccessStatus)),
                row.ScorePercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                row.BestScorePercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                Math.Round(row.TotalTimeSecond / 60.0, 1).ToString(CultureInfo.InvariantCulture),
                row.AttemptCount.ToString(CultureInfo.InvariantCulture),
                row.IsSuspended ? "Có" : "",
                row.LastAccessedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "",
                row.CompletedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? ""));

        // BOM để Excel đọc đúng tiếng Việt khi mở thẳng tệp CSV.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Result.FileDownload(bytes, "text/csv; charset=utf-8", $"ket-qua-scorm-{course.Code}.csv");

        static string Csv(string? value)
        {
            var text = value ?? "";
            // Chặn công thức Excel (CSV injection): ô bắt đầu bằng = + - @ bị Excel chạy như công thức.
            if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
            return text.Contains(',') || text.Contains('"') || text.Contains('\n') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
        }
    }

    private static string CompletionLabel(string value) => value switch
    {
        "completed" => "Hoàn thành",
        "incomplete" => "Chưa hoàn thành",
        _ => "Chưa học",
    };

    private static string SuccessLabel(string value) => value switch
    {
        "passed" => "Đạt",
        "failed" => "Không đạt",
        _ => "",
    };

    private static Dictionary<string, string> ParseCmi(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string? Get(Dictionary<string, string> cmi, string key)
        => cmi.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static readonly Regex InteractionKeyRegex = new(@"^cmi\.interactions\.(\d+)\.(.+)$", RegexOptions.Compiled);

    private static List<ScormInteractionDto> ReadInteractionList(Dictionary<string, string> cmi)
    {
        var byIndex = new SortedDictionary<int, Dictionary<string, string>>();
        foreach (var (key, value) in cmi)
        {
            var match = InteractionKeyRegex.Match(key);
            if (!match.Success) continue;
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!byIndex.TryGetValue(index, out var fields)) byIndex[index] = fields = new Dictionary<string, string>();
            fields[match.Groups[2].Value] = value;
        }

        return byIndex.Values.Select(f => new ScormInteractionDto(
            f.GetValueOrDefault("id") ?? "",
            f.GetValueOrDefault("type"),
            f.GetValueOrDefault("description"),
            f.GetValueOrDefault("learner_response") ?? f.GetValueOrDefault("student_response"),
            f.GetValueOrDefault("correct_responses.0.pattern"),
            f.GetValueOrDefault("result"),
            f.GetValueOrDefault("weighting"),
            f.GetValueOrDefault("latency"),
            f.GetValueOrDefault("timestamp") ?? f.GetValueOrDefault("time"))).ToList();
    }

    // ==================== XUẤT GÓI (ĐẦU RA) ====================

    public async Task<Result> GetExportPreview(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var preview = await _exporter.BuildPreviewAsync(courseId);
        return preview is null ? Result.NotFound(new MessageResponse("Không tìm thấy khóa học.")) : Result.Success(preview);
    }

    public async Task<Result> GetManifest(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var xml = await _exporter.BuildManifestAsync(courseId);
        return xml is null ? Result.NotFound(new MessageResponse("Không tìm thấy khóa học.")) : Result.Success(new { Xml = xml });
    }

    public async Task<Result> ExportCourse(int courseId)
    {
        if (!_currentUser.IsContentViewer) return Result.Forbidden();
        var export = await _exporter.ExportAsync(courseId);
        if (export is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        if (export.Value.ErrorMessage is string error) return Invalid(error);
        return Result.FileStreamDownload(export.Value.Stream!, "application/zip", export.Value.FileName);
    }
}
