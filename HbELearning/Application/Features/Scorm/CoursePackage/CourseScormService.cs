// File: Application/Features/Scorm/CoursePackage/CourseScormService.cs
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using HbELearning.Application.Features.Scorm.Model;
using HbELearning.Application.Features.Scorm.Sequencing;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Scorm.CoursePackage;

/// <summary>Kết quả có thẩm quyền (chấm trên máy chủ) của các SCO hệ thống trong một lượt học khóa học.</summary>
public sealed class CourseAuthority
{
    /// <summary>Hoạt động lá L{id} / Q{id} / S{id} → trạng thái lấy từ LessonProgress / QuizAttempt / ScenarioAttempt.</summary>
    public Dictionary<string, ScormAuthoritativeStatus> StatusById { get; } = new(StringComparer.Ordinal);

    /// <summary>Hoạt động đã "xong" theo đúng quy tắc mở khóa của lộ trình: bài học đã hoàn thành, bài kiểm tra /
    /// mô phỏng đã ĐẠT ít nhất một lần.</summary>
    public HashSet<string> DoneIdSet { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Khóa học = MỘT gói SCORM 2004 4th Edition của chính hệ thống (ScormPackage loại Course). Cây hoạt động của gói
/// được dựng từ cấu trúc người soạn đã dựng (CourseOrganizationBuilder) và dựng lại mỗi khi mở trình phát, để mọi
/// chỉnh sửa cấu trúc có hiệu lực ngay.
///
/// Bài học / bài kiểm tra / mô phỏng là SCO do hệ thống phục vụ — kết quả thật nằm ở bảng nghiệp vụ, nên trước mỗi
/// bước sequencing lớp này nạp kết quả đó vào mô hình theo dõi (<see cref="ScormSequencer.ApplyAuthoritativeStatus"/>)
/// và tự duy trì các mục tiêu toàn cục khóa tuần tự "hbe-done-*" (<see cref="ScormSequencer.AfterRollup"/>).
/// </summary>
public class CourseScormService
{
    private static readonly Regex SystemLeafRegex = new(@"^(?<kind>[LQS])(?<id>\d+)$", RegexOptions.Compiled);
    private static readonly Regex PackageClusterRegex = new(@"^P\d+$", RegexOptions.Compiled);

    /// <summary>Hai lần mở trình phát cùng lúc trên một khóa học chưa có gói thì chỉ được tạo MỘT gói.</summary>
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> EnsureLockByCourse = new();

    private readonly IAppDbContext _db;
    private readonly CourseOrganizationBuilder _builder;
    private readonly IMemoryCache _cache;

    public CourseScormService(IAppDbContext db, CourseOrganizationBuilder builder, IMemoryCache cache)
    {
        _db = db;
        _builder = builder;
        _cache = cache;
    }

    public static string TreeCacheKey(int packageId) => $"scorm-tree:{packageId}";

    // ==================== GÓI CỦA KHÓA HỌC ====================

    /// <summary>Gói SCORM của khóa học, cây hoạt động khớp với cấu trúc hiện tại (tạo mới nếu chưa có).</summary>
    public async Task<ScormPackage> EnsureCoursePackageAsync(int courseId)
    {
        var gate = EnsureLockByCourse.GetOrAdd(courseId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var course = await _db.Course.AsNoTracking().FirstAsync(x => x.Id == courseId);
            var organization = await _builder.BuildAsync(courseId);
            var tree = organization.Tree;
            var json = JsonSerializer.Serialize(tree);
            var leafList = tree.Preorder().Where(a => a.IsLeaf && a != tree.Root).ToList();

            var package = await _db.ScormPackage.FirstOrDefaultAsync(x => x.CourseId == courseId && x.Kind == ScormPackageKind.Course);
            if (package is null)
            {
                package = new ScormPackage
                {
                    CourseId = courseId,
                    Kind = ScormPackageKind.Course,
                    // Gói của khóa học không có tệp trên đĩa — nội dung là các trang SCO của hệ thống và kho của các gói
                    // nhập ngoài. Khóa kho vẫn phải là một GUID để giữ đúng ràng buộc duy nhất của cột.
                    StorageKey = Guid.NewGuid().ToString("N"),
                    Title = course.Title,
                    Version = ScormVersion.Scorm2004,
                    SchemaVersion = tree.SchemaVersion,
                    ManifestIdentifier = tree.ManifestIdentifier,
                    ActivityTreeJson = json,
                    LaunchableCount = leafList.Count,
                    ScoCount = leafList.Count(a => a.IsSco),
                    DurationMinutes = course.DurationMinutes,
                    CreatedById = course.CreatedById,
                    UpdatedAt = DateTime.Now,
                };
                _db.ScormPackage.Add(package);
                await _db.SaveChangesAsync();
                return package;
            }

            if (package.ActivityTreeJson != json || package.Title != course.Title)
            {
                package.ActivityTreeJson = json;
                package.Title = course.Title;
                package.ManifestIdentifier = tree.ManifestIdentifier;
                package.LaunchableCount = leafList.Count;
                package.ScoCount = leafList.Count(a => a.IsSco);
                package.DurationMinutes = course.DurationMinutes;
                package.UpdatedAt = DateTime.Now;
                await _db.SaveChangesAsync();
                _cache.Remove(TreeCacheKey(package.Id));
            }
            return package;
        }
        finally
        {
            gate.Release();
        }
    }

    // ==================== KẾT QUẢ CÓ THẨM QUYỀN ====================

    public async Task<CourseAuthority> LoadAuthorityAsync(int enrollmentId, ScormActivityTree tree)
    {
        var authority = new CourseAuthority();
        var lessonIdSet = new HashSet<int>();
        var quizIdSet = new HashSet<int>();
        var sceneIdSet = new HashSet<int>();
        foreach (var activity in tree.Preorder())
        {
            var match = SystemLeafRegex.Match(activity.Id);
            if (!match.Success || !activity.IsLeaf) continue;
            var id = int.Parse(match.Groups["id"].Value);
            (match.Groups["kind"].Value switch { "L" => lessonIdSet, "Q" => quizIdSet, _ => sceneIdSet }).Add(id);
        }

        if (lessonIdSet.Count > 0)
        {
            var progressDict = await _db.LessonProgress.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollmentId && lessonIdSet.Contains(x.LessonId))
                .ToDictionaryAsync(x => x.LessonId);
            foreach (var lessonId in lessonIdSet)
            {
                progressDict.TryGetValue(lessonId, out var progress);
                var attempted = progress is not null && (progress.StartedAt is not null || progress.Status != ProgressStatus.NotStarted);
                var completed = progress?.Status == ProgressStatus.Completed;
                // Bài học không chấm đạt / trượt: hoàn thành là đạt — để rollup và chuỗi tuần tự có trạng thái mà đọc.
                Put(authority, $"L{lessonId}", new ScormAuthoritativeStatus(attempted, attempted ? completed : null, completed ? true : null, null), completed);
            }
        }

        if (quizIdSet.Count > 0)
        {
            var attemptList = await _db.QuizAttempt.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollmentId && quizIdSet.Contains(x.QuizId))
                .Select(x => new { x.QuizId, x.SubmittedAt, x.Score, x.IsPassed })
                .ToListAsync();
            foreach (var quizId in quizIdSet)
            {
                var mine = attemptList.Where(a => a.QuizId == quizId).ToList();
                var submitted = mine.Where(a => a.SubmittedAt != null).ToList();
                var passed = submitted.Any(a => a.IsPassed);
                // Lấy kết quả TỐT NHẤT — khớp lộ trình từ trước (đã đạt một lần là xong, điểm là điểm cao nhất).
                Put(authority, $"Q{quizId}", new ScormAuthoritativeStatus(
                    mine.Count > 0,
                    submitted.Count > 0 ? true : mine.Count > 0 ? false : null,
                    passed ? true : submitted.Count > 0 ? false : null,
                    submitted.Count > 0 ? Math.Clamp((double)submitted.Max(a => a.Score) / 100.0, 0, 1) : null), passed);
            }
        }

        if (sceneIdSet.Count > 0)
        {
            var attemptList = await _db.ScenarioAttempt.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollmentId && sceneIdSet.Contains(x.ScenarioSceneId))
                .Select(x => new { x.ScenarioSceneId, x.Status, x.Score, x.MaxScore, x.IsPassed })
                .ToListAsync();
            foreach (var sceneId in sceneIdSet)
            {
                var mine = attemptList.Where(a => a.ScenarioSceneId == sceneId).ToList();
                var finished = mine.Where(a => a.Status == ScenarioAttemptStatus.Completed).ToList();
                var passed = finished.Any(a => a.IsPassed);
                // Điểm mô phỏng có thể âm — điểm chuẩn hóa của SCORM cho phép -1..1.
                double? measure = finished.Count > 0
                    ? finished.Max(a => a.MaxScore > 0 ? Math.Clamp((double)a.Score / a.MaxScore, -1, 1) : 0)
                    : null;
                Put(authority, $"S{sceneId}", new ScormAuthoritativeStatus(
                    mine.Count > 0,
                    finished.Count > 0 ? true : mine.Count > 0 ? false : null,
                    passed ? true : finished.Count > 0 ? false : null,
                    measure), passed);
            }
        }
        return authority;

        static void Put(CourseAuthority target, string id, ScormAuthoritativeStatus status, bool done)
        {
            target.StatusById[id] = status;
            if (done) target.DoneIdSet.Add(id);
        }
    }

    /// <summary>
    /// Engine sequencing cho một lượt học khóa học: nạp kết quả có thẩm quyền (nếu là học viên thật) và gắn bộ duy trì
    /// mục tiêu "hbe-done-*". <paramref name="authority"/> null = bản xem thử — "xong" tính theo chính dữ liệu SCO báo.
    /// </summary>
    public static ScormSequencer CreateSequencer(ScormActivityTree tree, ScormSequencingState state, CourseAuthority? authority)
    {
        var hook = DoneObjectiveHook(tree, authority);
        var sequencer = new ScormSequencer(tree, state) { AfterRollup = hook };
        if (authority is not null) ApplyAuthority(sequencer, authority);
        hook(sequencer);
        return sequencer;
    }

    public static void ApplyAuthority(ScormSequencer sequencer, CourseAuthority authority)
    {
        foreach (var (activityId, status) in authority.StatusById)
            sequencer.ApplyAuthoritativeStatus(activityId, status);
    }

    /// <summary>
    /// Mục tiêu toàn cục "hbe-done-{hoạt động}" của chuỗi học tuần tự do LMS ghi, theo đúng quy tắc mở khóa của lộ
    /// trình: SCO hệ thống xét kết quả chấm trên máy chủ; gói nhập ngoài xét kết quả tốt nhất từng đạt (đạt, hoặc
    /// hoàn thành mà không bị đánh trượt). Gói xuất ra LMS khác thì để chính hoạt động ghi qua mapInfo như thường.
    /// </summary>
    private static Action<ScormSequencer> DoneObjectiveHook(ScormActivityTree tree, CourseAuthority? authority)
    {
        var writerList = new List<(string ActivityId, string GlobalId)>();
        foreach (var activity in tree.Preorder())
            foreach (var map in activity.Sequencing.PrimaryObjective?.MapList ?? new List<ObjectiveMapDefinition>())
                if (map.WriteSatisfiedStatus && map.TargetObjectiveId.StartsWith("hbe-done-", StringComparison.Ordinal))
                    writerList.Add((activity.Id, map.TargetObjectiveId));

        return sequencer =>
        {
            foreach (var (activityId, globalId) in writerList)
            {
                bool done;
                if (authority is not null && authority.StatusById.ContainsKey(activityId))
                    done = authority.DoneIdSet.Contains(activityId);
                else
                {
                    sequencer.State.ActivityById.TryGetValue(activityId, out var state);
                    done = state is not null && (state.EverSatisfied || (state.EverCompleted && !state.EverFailed));
                }
                if (!sequencer.State.GlobalObjectiveById.TryGetValue(globalId, out var global))
                    sequencer.State.GlobalObjectiveById[globalId] = global = new ObjectiveState();
                global.ProgressStatus = true;
                global.SatisfiedStatus = done;
            }
        };
    }

    // ==================== TIỆN ÍCH ====================

    /// <summary>Hoạt động là SCO do hệ thống phục vụ (trang trong /pages) — cần nối thêm ngữ cảnh lượt học.</summary>
    public static bool IsInternalLaunch(string launchUrl) => launchUrl.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Địa chỉ mở SCO hệ thống kèm ngữ cảnh: lượt học của học viên, hoặc khóa học đang xem thử.</summary>
    public static string WithContext(string launchUrl, int? enrollmentId, int courseId)
        => launchUrl + (launchUrl.Contains('?') ? "&" : "?")
           + (enrollmentId is int id ? $"enrollmentId={id}" : $"preview=1&courseId={courseId}");

    /// <summary>Chuỗi so sánh kết quả các gói nhập ngoài trong lượt học — đổi thì tiến độ khóa học phải tính lại
    /// (bài học / bài kiểm tra / mô phỏng tự kích hoạt tính lại ở dịch vụ của chúng).</summary>
    public static string PackageOutcomeKey(ScormSequencingState state)
        => string.Join("|", state.ActivityById
            .Where(x => PackageClusterRegex.IsMatch(x.Key))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}:{x.Value.EverCompleted}{x.Value.EverSatisfied}{x.Value.EverFailed}{x.Value.BestMeasure}{x.Value.AttemptCount}"));

    /// <summary>Kết quả một gói nhập ngoài trong lượt học khóa học (đọc thẳng trạng thái đã lưu, không cần engine).</summary>
    public static ActivityState? PackageState(string? stateJson, int packageId)
    {
        if (string.IsNullOrEmpty(stateJson)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<ScormSequencingState>(stateJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return state is not null && state.ActivityById.TryGetValue($"P{packageId}", out var activity) ? activity : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
