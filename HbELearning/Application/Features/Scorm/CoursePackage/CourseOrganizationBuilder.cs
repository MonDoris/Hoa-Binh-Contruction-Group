// File: Application/Features/Scorm/CoursePackage/CourseOrganizationBuilder.cs
using System.Text.Json;
using HbELearning.Application.Features.Scorm.Model;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Scorm.CoursePackage;

/// <summary>Tài nguyên (SCO) mà một hoạt động trong cây của khóa học trỏ tới.</summary>
public enum CourseResourceKind { Lesson, Quiz, Simulation, Package }

public sealed record CourseResourceRef(CourseResourceKind Kind, int Id, string Title, ScormVersion? PackageVersion = null);

/// <summary>Cây hoạt động SCORM của một khóa học + bảng tra hoạt động → tài nguyên.</summary>
public sealed class CourseOrganization
{
    public required ScormActivityTree Tree { get; init; }

    /// <summary>Hoạt động lá của hệ thống (L/Q/S) và cụm gói nhập ngoài (P) → tài nguyên.</summary>
    public Dictionary<string, CourseResourceRef> ResourceByActivityId { get; } = new(StringComparer.Ordinal);

    /// <summary>Mục bị bỏ ra khỏi cây (xuất gói: mô phỏng 3D, gói SCORM 1.2) — kèm lý do.</summary>
    public List<(CourseResourceRef Resource, string Reason)> SkippedList { get; } = new();
}

/// <summary>
/// Dựng cây hoạt động SCORM 2004 4th Edition của một khóa học từ cấu trúc người soạn đã dựng (lộ trình + chương +
/// mục). Đây là chỗ DUY NHẤT dịch thiết lập của người soạn sang ngôn ngữ của chuẩn:
///
/// <list type="bullet">
/// <item>Khóa học → &lt;organization&gt;, controlMode choice + flow (học viên vừa chọn mục lục vừa bấm Tiếp được).</item>
/// <item>Chương → &lt;item&gt; cụm; bài học / bài kiểm tra / mô phỏng 3D → &lt;item&gt; lá trỏ tới một SCO do hệ
///   thống phục vụ; gói SCORM nhập ngoài → nguyên cây của gói, nhúng làm một cụm.</item>
/// <item>"Học tuần tự" → luật tiền điều kiện <c>disabled</c> đọc mục tiêu toàn cục mà mục bắt buộc phía trước ghi ra
///   (đúng cách các gói SCORM "forced sequential" vẫn làm).</item>
/// <item>Lịch mở / hạn chót → limitConditions beginTimeLimit / endTimeLimit; giới hạn lượt → attemptLimit;
///   mục không bắt buộc → không góp vào rollup; bài thi cuối khóa → đạt theo điểm (satisfiedByMeasure).</item>
/// </list>
///
/// Cùng một cây được dùng để PHÁT (trình phát SCORM của hệ thống) và để XUẤT (ScormManifestWriter ghi ra
/// imsmanifest.xml) — nên khóa học trong hệ thống và gói mang đi LMS khác hành xử giống hệt nhau.
/// </summary>
public class CourseOrganizationBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IAppDbContext _db;

    public CourseOrganizationBuilder(IAppDbContext db) => _db = db;

    public static string RootId(int courseId) => $"ORG-C{courseId}";
    public static string ModuleId(int pathItemId) => $"M{pathItemId}";
    public static string ActivityId(CourseResourceKind kind, int id) => kind switch
    {
        CourseResourceKind.Lesson => $"L{id}",
        CourseResourceKind.Quiz => $"Q{id}",
        CourseResourceKind.Simulation => $"S{id}",
        _ => $"P{id}",
    };

    /// <summary>Địa chỉ SCO do hệ thống phục vụ cho từng loại tài nguyên (tương đối gốc web).</summary>
    public static string InternalLaunchUrl(CourseResourceKind kind, int id) => kind switch
    {
        CourseResourceKind.Lesson => $"/pages/sco-lesson.html?lessonId={id}",
        CourseResourceKind.Quiz => $"/pages/sco-quiz.html?quizId={id}",
        CourseResourceKind.Simulation => $"/pages/simulation.html?sco=1&scenarioSceneId={id}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <param name="forExport">true = dựng cho gói xuất: bỏ mô phỏng 3D (chạy trên engine 3D của hệ thống) và gói
    /// SCORM 1.2 (SCO 1.2 tìm bộ API "API" mà LMS phát gói 2004 không cung cấp).</param>
    public async Task<CourseOrganization> BuildAsync(int courseId, bool forExport = false)
    {
        var course = await _db.Course.AsNoTracking().FirstAsync(x => x.Id == courseId);
        // Khóa học chưa dựng cấu trúc: dùng cấu trúc mặc định — đúng thứ tự lộ trình học viên đang thấy.
        var (path, itemList) = await DefaultOrganization.LoadAsync(_db, courseId);

        var lessonDict = await _db.Lesson.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive).ToDictionaryAsync(x => x.Id);
        var quizDict = await _db.Quiz.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive).ToDictionaryAsync(x => x.Id);
        // Mô phỏng chưa có tình huống nào thì chưa giao được (không bao giờ đạt) — bỏ khỏi cây như một mục ẩn.
        var sceneDict = await _db.ScenarioScene.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive && x.NodeList.Count > 0).ToDictionaryAsync(x => x.Id);
        var packageDict = await _db.ScormPackage.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.Kind == ScormPackageKind.Imported).ToDictionaryAsync(x => x.Id);
        var finalExamIdSet = quizDict.Values.Where(q => q.IsFinalExam).Select(q => q.Id).ToHashSet();

        var root = new ScormActivityDefinition
        {
            Id = RootId(courseId),
            Title = course.Title,
            Sequencing = new SequencingDefinition { ControlChoice = true, ControlFlow = true },
        };
        var tree = new ScormActivityTree
        {
            Version = ScormVersion.Scorm2004,
            SchemaVersion = "2004 4th Edition",
            ManifestIdentifier = $"HBE-{course.Code}",
            OrganizationId = root.Id,
            ObjectivesGlobalToSystem = false,
            SharedDataGlobalToSystem = false,
            Root = root,
        };
        var organization = new CourseOrganization { Tree = tree };

        // ---------- Dựng nút theo cấu trúc Chương / Mục ----------
        // Lá được dựng kèm item gốc để bước sau (chuỗi tuần tự) biết mục nào bắt buộc / có lịch.
        var leafList = new List<(ScormActivityDefinition Activity, PathItem Item)>();
        // Một tài nguyên chỉ được là MỘT hoạt động: định danh hoạt động lấy theo tài nguyên (L5, Q3...), hai mục cùng
        // trỏ một bài học sẽ đụng định danh — giữ mục đứng trước.
        var usedIdSet = new HashSet<string>(StringComparer.Ordinal);

        void AddChildren(ScormActivityDefinition parent, IEnumerable<OrganizationNode> nodeList)
        {
            foreach (var node in nodeList)
            {
                var item = node.Item;
                if (node.IsModule)
                {
                    var module = new ScormActivityDefinition
                    {
                        Id = ModuleId(item.Id),
                        Title = string.IsNullOrWhiteSpace(item.Title) ? "Chương" : item.Title!,
                        IsVisible = item.IsVisible,
                        Sequencing = new SequencingDefinition { ControlChoice = true, ControlFlow = true },
                    };
                    ApplyCommonSettings(module, item, measureWeight: 1, applyEndLimit: true);
                    AddChildren(module, node.Children);
                    // Chương không chứa mục nào tính điểm (toàn bài học) thì không có điểm để gộp — để trọng số 1 là nó
                    // vẫn bị tính vào mẫu số ở cấp trên, kéo điểm khóa học xuống.
                    if (!module.Children.Any(c => c.Sequencing.ObjectiveMeasureWeight > 0))
                        module.Sequencing.ObjectiveMeasureWeight = 0;
                    // Chương rỗng là cụm không có gì để giao — bỏ, để luồng Tiếp không vấp phải nó.
                    if (module.Children.Count > 0) parent.Children.Add(module);
                    continue;
                }

                var activity = BuildLeaf(item, lessonDict, quizDict, sceneDict, packageDict, finalExamIdSet, course, forExport, organization);
                if (activity is null || !usedIdSet.Add(activity.Id)) continue;
                parent.Children.Add(activity);
                leafList.Add((activity, item));
            }
        }
        AddChildren(root, OrganizationOrder.BuildTree(itemList));

        // ---------- Học tuần tự ----------
        if (path?.IsSequential ?? true) ApplySequentialChain(leafList);

        return organization;
    }

    // ==================== MỤC LÁ ====================

    private ScormActivityDefinition? BuildLeaf(PathItem item,
        Dictionary<int, Lesson> lessonDict, Dictionary<int, Quiz> quizDict, Dictionary<int, ScenarioScene> sceneDict,
        Dictionary<int, ScormPackage> packageDict, HashSet<int> finalExamIdSet, Course course,
        bool forExport, CourseOrganization organization)
    {
        var anyFinalExam = finalExamIdSet.Count > 0;
        switch (item.ItemType)
        {
            case PathItemType.Lesson when item.LessonId is int lessonId && lessonDict.TryGetValue(lessonId, out var lesson):
            {
                var activity = NewSco(CourseResourceKind.Lesson, lesson.Id, Pick(item.Title, lesson.Title), item);
                // Bài học không chấm điểm: trọng số 0, không thì điểm khóa học bị kéo xuống (chuẩn tính cả con
                // không có điểm vào mẫu số khi gộp điểm).
                ApplyCommonSettings(activity, item, measureWeight: 0, applyEndLimit: false);
                organization.ResourceByActivityId[activity.Id] = new CourseResourceRef(CourseResourceKind.Lesson, lesson.Id, lesson.Title);
                return activity;
            }
            case PathItemType.Quiz when item.QuizId is int quizId && quizDict.TryGetValue(quizId, out var quiz):
            {
                var activity = NewSco(CourseResourceKind.Quiz, quiz.Id, Pick(item.Title, quiz.Title), item);
                var isFinal = finalExamIdSet.Contains(quiz.Id);
                // Có bài thi cuối khóa thì chỉ bài đó tính điểm khóa học — khớp cách tính điểm cuối khóa từ trước.
                ApplyCommonSettings(activity, item, measureWeight: !anyFinalExam || isFinal ? 1 : 0, applyEndLimit: true);
                if (isFinal)
                {
                    // Thi cuối khóa: đạt khi điểm >= điểm đạt của khóa học (và của chính bài thi, lấy mức cao hơn).
                    var primary = activity.Sequencing.PrimaryObjective!;
                    primary.SatisfiedByMeasure = true;
                    primary.MinNormalizedMeasure = (double)Math.Max(quiz.PassScore, course.PassScore) / 100.0;
                }
                organization.ResourceByActivityId[activity.Id] = new CourseResourceRef(CourseResourceKind.Quiz, quiz.Id, quiz.Title);
                return activity;
            }
            case PathItemType.Simulation when item.ScenarioSceneId is int sceneId && sceneDict.TryGetValue(sceneId, out var scene):
            {
                var reference = new CourseResourceRef(CourseResourceKind.Simulation, scene.Id, scene.Title);
                if (forExport)
                {
                    organization.SkippedList.Add((reference, "Mô phỏng 3D chạy trên engine 3D của HB E-Learning — không đóng gói ra LMS khác được."));
                    return null;
                }
                var activity = NewSco(CourseResourceKind.Simulation, scene.Id, Pick(item.Title, scene.Title), item);
                ApplyCommonSettings(activity, item, measureWeight: 0, applyEndLimit: true);
                organization.ResourceByActivityId[activity.Id] = reference;
                return activity;
            }
            case PathItemType.Scorm when item.ScormPackageId is int packageId && packageDict.TryGetValue(packageId, out var package):
            {
                var reference = new CourseResourceRef(CourseResourceKind.Package, package.Id, package.Title, package.Version);
                if (forExport && package.Version == ScormVersion.Scorm12)
                {
                    organization.SkippedList.Add((reference, "Gói SCORM 1.2 không nhúng được vào gói 2004 — tải gói gốc để mang đi riêng."));
                    return null;
                }
                var cluster = EmbedPackage(package, Pick(item.Title, package.Title));
                ApplyCommonSettings(cluster, item, measureWeight: anyFinalExam ? 0 : 1, applyEndLimit: true);
                // Ngưỡng hoàn thành theo tiến độ chỉ có nghĩa với gói nhập ngoài: cụm hoàn thành khi tiến độ gộp từ các
                // SCO của gói (progress measure rollup) đạt ngưỡng. Bài học / bài kiểm tra của hệ thống tự có điều kiện
                // hoàn thành riêng, chấm trên máy chủ.
                if (item.CompletionThreshold is decimal threshold && threshold > 0)
                    cluster.CompletionThreshold = new CompletionThresholdDefinition
                    {
                        CompletedByMeasure = true,
                        MinProgressMeasure = (double)Math.Clamp(threshold, 0m, 1m),
                    };
                organization.ResourceByActivityId[cluster.Id] = reference;
                return cluster;
            }
            default:
                return null; // mục trỏ tới tài nguyên đã ẩn / đã xóa
        }
    }

    private static string Pick(string? custom, string fallback) => string.IsNullOrWhiteSpace(custom) ? fallback : custom.Trim();

    private static ScormActivityDefinition NewSco(CourseResourceKind kind, int id, string title, PathItem item)
    {
        var activity = new ScormActivityDefinition
        {
            Id = ActivityId(kind, id),
            Title = title,
            IsVisible = item.IsVisible,
            IsSco = true,
            LaunchUrl = InternalLaunchUrl(kind, id),
            Sequencing = new SequencingDefinition
            {
                // SCO của hệ thống tự báo hoàn thành và kết quả — LMS không được tự điền "hoàn thành" khi học viên
                // chỉ mở ra rồi đi.
                CompletionSetByContent = true,
                ObjectiveSetByContent = true,
            },
        };
        activity.Sequencing.ObjectiveList.Add(new ObjectiveDefinition { IsPrimary = true, Id = $"{activity.Id}-primary" });
        return activity;
    }

    /// <summary>Thiết lập chung của mục: bắt buộc, giới hạn lượt, lịch mở / hạn chót, trọng số điểm.</summary>
    private static void ApplyCommonSettings(ScormActivityDefinition activity, PathItem item, double measureWeight, bool applyEndLimit)
    {
        var sequencing = activity.Sequencing;
        if (sequencing.PrimaryObjective is null)
            sequencing.ObjectiveList.Insert(0, new ObjectiveDefinition { IsPrimary = true, Id = $"{activity.Id}-primary" });
        else if (string.IsNullOrEmpty(sequencing.PrimaryObjective.Id))
            sequencing.PrimaryObjective.Id = $"{activity.Id}-primary";

        if (!item.IsRequired)
        {
            sequencing.RollupObjectiveSatisfied = false;
            sequencing.RollupProgressCompletion = false;
        }
        sequencing.ObjectiveMeasureWeight = measureWeight;
        if (item.AttemptLimit is int limit && limit > 0) sequencing.AttemptLimit = limit;
        if (item.StartDate is DateTime start) sequencing.BeginTimeLimit = start;
        // Bài học chỉ chặn TRƯỚC ngày mở, không khóa lại sau ngày kết thúc (ngày kết thúc chỉ để hiển thị lịch).
        if (applyEndLimit && item.EndDate is DateTime end) sequencing.EndTimeLimit = end;
    }

    /// <summary>
    /// Học tuần tự: mỗi mục bắt buộc ghi trạng thái đạt ra một mục tiêu toàn cục; mục phía sau có luật
    /// "disabled" khi mục bắt buộc ngay trước nó chưa đạt.
    ///
    /// Mục CÓ LỊCH thì lịch là điều kiện mở duy nhất (không xét tuần tự) — giữ đúng quy tắc lộ trình từ trước. Vì mục
    /// có lịch không đòi mục trước nó, mục đứng sau phải đọc thêm qua nó tới mục bắt buộc không-lịch gần nhất — nếu
    /// không thì một mục có lịch thành lỗ hổng để vượt rào cả chuỗi.
    /// </summary>
    private static void ApplySequentialChain(List<(ScormActivityDefinition Activity, PathItem Item)> leafList)
    {
        string GlobalOf(ScormActivityDefinition activity) => $"hbe-done-{activity.Id}";
        bool HasSchedule(PathItem item) => item.StartDate is not null || item.EndDate is not null;

        foreach (var (activity, item) in leafList.Where(x => x.Item.IsRequired))
        {
            var primary = activity.Sequencing.PrimaryObjective!;
            primary.MapList.Add(new ObjectiveMapDefinition
            {
                TargetObjectiveId = GlobalOf(activity),
                ReadSatisfiedStatus = false,
                ReadNormalizedMeasure = false,
                WriteSatisfiedStatus = true,
            });
        }

        for (var index = 0; index < leafList.Count; index++)
        {
            var (activity, item) = leafList[index];
            if (HasSchedule(item)) continue;

            var referenceList = new List<ScormActivityDefinition>();
            for (var back = index - 1; back >= 0; back--)
            {
                var (previous, previousItem) = leafList[back];
                if (!previousItem.IsRequired) continue;
                referenceList.Add(previous);
                if (!HasSchedule(previousItem)) break; // mục không-lịch đã tự đòi các mục trước nó
            }
            if (referenceList.Count == 0) continue;

            var rule = new SequencingRuleDefinition { ConditionCombination = "any", Action = "disabled" };
            var number = 0;
            foreach (var previous in referenceList)
            {
                var objectiveId = $"hbe-prev-{++number}";
                activity.Sequencing.ObjectiveList.Add(new ObjectiveDefinition
                {
                    Id = objectiveId,
                    MapList = { new ObjectiveMapDefinition { TargetObjectiveId = GlobalOf(previous), ReadSatisfiedStatus = true, ReadNormalizedMeasure = false } },
                });
                rule.ConditionList.Add(new RuleConditionDefinition { Condition = "satisfied", ReferencedObjective = objectiveId, Operator = "not" });
                rule.ConditionList.Add(new RuleConditionDefinition { Condition = "objectiveStatusKnown", ReferencedObjective = objectiveId, Operator = "not" });
            }
            activity.Sequencing.PreConditionRuleList.Insert(0, rule);
        }
    }

    // ==================== GÓI NHẬP NGOÀI ====================

    /// <summary>Nhúng nguyên cây của một gói nhập ngoài làm một cụm: mọi định danh (hoạt động, mục tiêu toàn cục, kho
    /// dữ liệu chia sẻ) được gắn tiền tố "P{id}" để không đụng nhau giữa các gói; địa chỉ nội dung trỏ thẳng vào kho
    /// của gói; SCO của gói 1.2 được đánh dấu để trình phát dựng đúng bộ API 1.2.</summary>
    private static ScormActivityDefinition EmbedPackage(ScormPackage package, string title)
    {
        var source = JsonSerializer.Deserialize<ScormActivityTree>(package.ActivityTreeJson, JsonOptions)!;
        var cluster = source.Root;
        var prefix = $"P{package.Id}";

        void Rewrite(ScormActivityDefinition activity, bool isRoot)
        {
            activity.Id = isRoot ? prefix : $"{prefix}-{activity.Id}";
            if (activity.LaunchUrl is string url && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith('/'))
                activity.LaunchUrl = $"/scorm-content/{package.StorageKey}/{url}";
            if (package.Version == ScormVersion.Scorm12 && activity.IsLeaf) activity.RuntimeVersion = "1.2";
            foreach (var objective in activity.Sequencing.ObjectiveList)
                foreach (var map in objective.MapList)
                    map.TargetObjectiveId = $"{prefix}:{map.TargetObjectiveId}";
            foreach (var data in activity.SharedDataList)
                data.TargetId = $"{prefix}:{data.TargetId}";
            foreach (var child in activity.Children) Rewrite(child, false);
        }

        Rewrite(cluster, true);
        cluster.Title = title;
        cluster.IsVisible = true;
        return cluster;
    }
}
