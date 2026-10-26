// File: Application/Features/LearningPaths/LearningPathService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.LearningPaths;

/// <summary>
/// Cấu trúc khóa học = SCORM Organization: LearningPath là thẻ &lt;organization&gt;, PathItem là &lt;item&gt; — chương
/// (Module, cụm) hoặc mục lá trỏ tới một SCO (bài học, bài kiểm tra, mô phỏng 3D, gói SCORM nhập ngoài). Thiết lập
/// của từng mục là phần sequencing của item (xem CourseOrganizationBuilder).
///
/// OrderIndex là thứ tự TIỀN THỨ TỰ của cả cây (duy nhất trong một lộ trình — có chỉ mục duy nhất), nên mọi thao tác
/// đổi chỗ đều đánh số lại cả cây qua <see cref="SaveOrderAsync"/>.
/// </summary>
public class LearningPathService : ILearningPathService
{
    private const string ModuleDefaultTitle = "Chương";

    private readonly IAppDbContext _db;

    public LearningPathService(IAppDbContext db)
    {
        _db = db;
    }

    // ==================== XEM ====================

    public async Task<Result> GetByCourse(int courseId)
    {
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var (path, itemList) = await DefaultOrganization.LoadAsync(_db, courseId);
        return Result.Success(await ToDtoAsync(course, path, itemList));
    }

    private async Task<LearningPathDto> ToDtoAsync(Course course, LearningPath? path, List<PathItem> itemList)
    {
        var courseId = course.Id;
        var lessonDict = await _db.Lesson.AsNoTracking().Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id);
        var quizDict = await _db.Quiz.AsNoTracking().Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id);
        var sceneDict = await _db.ScenarioScene.AsNoTracking().Where(x => x.CourseId == courseId)
            .Select(x => new { x.Id, x.Title, x.LessonId, x.IsActive, NodeCount = x.NodeList.Count })
            .ToDictionaryAsync(x => x.Id);
        var packageDict = await _db.ScormPackage.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.Kind == ScormPackageKind.Imported).ToDictionaryAsync(x => x.Id);

        var isVirtual = path is null || path.PathItemList.Count == 0;
        var tree = OrganizationOrder.BuildTree(itemList);
        var result = new List<PathItemDto>();

        // Mục lá có giao được cho học viên không — chương chỉ hiện khi bên trong còn ít nhất một mục giao được.
        bool Deliverable(PathItem item) => item.ItemType switch
        {
            PathItemType.Lesson => item.LessonId is int l && lessonDict.TryGetValue(l, out var lesson) && lesson.IsActive,
            PathItemType.Quiz => item.QuizId is int q && quizDict.TryGetValue(q, out var quiz) && quiz.IsActive,
            PathItemType.Simulation => item.ScenarioSceneId is int s && sceneDict.TryGetValue(s, out var scene) && scene.IsActive && scene.NodeCount > 0,
            PathItemType.Scorm => item.ScormPackageId is int p && packageDict.ContainsKey(p),
            _ => false,
        };
        bool HasDeliverable(OrganizationNode node) => node.IsModule ? node.Children.Any(HasDeliverable) : Deliverable(node.Item);

        foreach (var node in OrganizationOrder.Preorder(tree))
        {
            var item = node.Item;
            string title;
            int duration = 0;
            int? parentLessonId = null;
            string? note = null;
            string? activityId = null;

            switch (item.ItemType)
            {
                case PathItemType.Module:
                    title = string.IsNullOrWhiteSpace(item.Title) ? ModuleDefaultTitle : item.Title!;
                    activityId = $"M{item.Id}";
                    if (!HasDeliverable(node)) note = "Chương chưa có nội dung giao được — học viên chưa thấy chương này.";
                    break;
                case PathItemType.Lesson when item.LessonId is int lessonId && lessonDict.TryGetValue(lessonId, out var lesson):
                    title = lesson.Title;
                    duration = lesson.DurationMinutes;
                    activityId = $"L{lesson.Id}";
                    if (!lesson.IsActive) note = "Bài học đang ẩn — học viên không thấy mục này.";
                    break;
                case PathItemType.Quiz when item.QuizId is int quizId && quizDict.TryGetValue(quizId, out var quiz):
                    title = quiz.Title;
                    duration = quiz.TimeLimitMinutes;
                    parentLessonId = quiz.LessonId;
                    activityId = $"Q{quiz.Id}";
                    if (!quiz.IsActive) note = "Bài kiểm tra đang ẩn — học viên không thấy mục này.";
                    break;
                case PathItemType.Simulation when item.ScenarioSceneId is int sceneId && sceneDict.TryGetValue(sceneId, out var scene):
                    title = scene.Title;
                    parentLessonId = scene.LessonId;
                    activityId = $"S{scene.Id}";
                    if (!scene.IsActive) note = "Mô phỏng đang ẩn — học viên không thấy mục này.";
                    else if (scene.NodeCount == 0) note = "Mô phỏng chưa có tình huống nào — chưa giao cho học viên.";
                    break;
                case PathItemType.Scorm when item.ScormPackageId is int packageId && packageDict.TryGetValue(packageId, out var package):
                    title = package.Title;
                    duration = package.DurationMinutes;
                    activityId = $"P{package.Id}";
                    break;
                default:
                    title = "(đã xóa)";
                    note = "Tài nguyên của mục này đã bị xóa.";
                    break;
            }

            result.Add(new PathItemDto(item.Id, item.ItemType.ToString(), item.LessonId, item.QuizId,
                item.ItemType != PathItemType.Module && !string.IsNullOrWhiteSpace(item.Title) ? item.Title! : title,
                duration, item.OrderIndex, item.IsRequired, item.StartDate, item.EndDate, parentLessonId,
                item.ScenarioSceneId, item.ScormPackageId, node.Parent?.Item.Id, item.Title, item.IsVisible,
                item.AttemptLimit, item.CompletionThreshold, activityId, node.Depth, note));
        }

        // Tài nguyên chưa nằm trong cấu trúc — học viên không thấy cho tới khi người soạn đưa vào.
        var unplaced = new List<UnplacedResourceDto>();
        if (!isVirtual)
        {
            var usedLesson = itemList.Where(x => x.LessonId.HasValue).Select(x => x.LessonId!.Value).ToHashSet();
            var usedQuiz = itemList.Where(x => x.QuizId.HasValue).Select(x => x.QuizId!.Value).ToHashSet();
            var usedScene = itemList.Where(x => x.ScenarioSceneId.HasValue).Select(x => x.ScenarioSceneId!.Value).ToHashSet();
            var usedPackage = itemList.Where(x => x.ScormPackageId.HasValue).Select(x => x.ScormPackageId!.Value).ToHashSet();
            unplaced.AddRange(lessonDict.Values.Where(x => x.IsActive && !usedLesson.Contains(x.Id)).OrderBy(x => x.OrderIndex)
                .Select(x => new UnplacedResourceDto(nameof(PathItemType.Lesson), x.Id, x.Title, null, null)));
            unplaced.AddRange(quizDict.Values.Where(x => x.IsActive && !usedQuiz.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new UnplacedResourceDto(nameof(PathItemType.Quiz), x.Id, x.Title, x.LessonId, null)));
            unplaced.AddRange(sceneDict.Values.Where(x => x.IsActive && !usedScene.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new UnplacedResourceDto(nameof(PathItemType.Simulation), x.Id, x.Title, x.LessonId,
                    x.NodeCount == 0 ? "Chưa có tình huống" : null)));
            unplaced.AddRange(packageDict.Values.Where(x => !usedPackage.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new UnplacedResourceDto(nameof(PathItemType.Scorm), x.Id, x.Title, null, null)));
        }

        return new LearningPathDto(path?.Id ?? 0, course.Id, course.Title,
            path?.Name ?? TrimTo($"Lộ trình {course.Title}", 250), path?.Description, path?.IsSequential ?? true,
            result, unplaced, isVirtual);
    }

    // ==================== LỘ TRÌNH (ORGANIZATION) ====================

    public async Task<Result> EnsureOrganization(int courseId)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == courseId))
            return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));
        await EnsurePathAsync(courseId);
        return await GetByCourse(courseId);
    }

    /// <summary>Lộ trình của khóa học, có sẵn mục: khóa học chưa từng dựng cấu trúc thì lưu cấu trúc mặc định xuống
    /// — từ đây người soạn sửa trên đúng thứ tự học viên đang thấy.</summary>
    private async Task<LearningPath> EnsurePathAsync(int courseId)
    {
        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.CourseId == courseId);
        if (path is null)
        {
            var title = await _db.Course.Where(x => x.Id == courseId).Select(x => x.Title).FirstAsync();
            path = new LearningPath { CourseId = courseId, Name = TrimTo($"Lộ trình {title}", 250), IsSequential = true };
            _db.LearningPath.Add(path);
            await _db.SaveChangesAsync();
        }

        if (path.PathItemList.Count == 0)
        {
            foreach (var item in await DefaultOrganization.BuildItemListAsync(_db, courseId))
                _db.PathItem.Add(new PathItem
                {
                    LearningPathId = path.Id,
                    ItemType = item.ItemType,
                    LessonId = item.LessonId,
                    QuizId = item.QuizId,
                    ScenarioSceneId = item.ScenarioSceneId,
                    ScormPackageId = item.ScormPackageId,
                    OrderIndex = item.OrderIndex,
                    IsRequired = true,
                });
            await _db.SaveChangesAsync();
        }
        return path;
    }

    public async Task<Result> Create(LearningPathRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == request.CourseId))
            return Result.Invalid(new MessageResponse("Khóa học không tồn tại."));

        if (await _db.LearningPath.AnyAsync(x => x.CourseId == request.CourseId))
            return Result.Conflict(new MessageResponse("Khóa học này đã có lộ trình."));

        var path = new LearningPath
        {
            CourseId = request.CourseId,
            Name = request.Name.Trim(),
            Description = request.Description,
            IsSequential = request.IsSequential
        };

        _db.LearningPath.Add(path);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse($"Đã tạo lộ trình #{path.Id}.", path.Id));
    }

    public async Task<Result> Update(int id, LearningPathRequest request)
    {
        var path = await _db.LearningPath.FirstOrDefaultAsync(x => x.Id == id);
        if (path is null) return Result.NotFound(new MessageResponse("Không tìm thấy lộ trình."));

        path.Name = request.Name.Trim();
        path.Description = request.Description;
        path.IsSequential = request.IsSequential;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu thiết lập cấu trúc khóa học."));
    }

    /// <summary>Dựng lại cấu trúc từ toàn bộ tài nguyên hiện có. Bài học có bài kiểm tra / mô phỏng đi kèm thành một
    /// chương (bài học → thực hành → đánh giá); bài học đứng một mình, gói SCORM và thi cuối khóa nằm ngay dưới gốc.</summary>
    public async Task<Result> AutoGenerate(int courseId)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.CourseId == courseId);
        if (path is null)
        {
            path = new LearningPath { CourseId = courseId, Name = TrimTo($"Lộ trình {course.Title}", 250), IsSequential = true };
            _db.LearningPath.Add(path);
            await _db.SaveChangesAsync();
        }
        else if (path.PathItemList.Count > 0)
        {
            // Gỡ quan hệ cha-con trước: khóa ngoại tự trỏ không lan truyền xóa, xóa chương cùng lượt với mục con của
            // nó thì thứ tự câu lệnh quyết định có vi phạm ràng buộc hay không.
            foreach (var item in path.PathItemList) item.ParentItemId = null;
            await _db.SaveChangesAsync();
            _db.PathItem.RemoveRange(path.PathItemList);
            await _db.SaveChangesAsync();
        }

        var defaultList = await DefaultOrganization.BuildItemListAsync(_db, courseId);
        var lessonTitleDict = await _db.Lesson.Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id, x => x.Title);
        var quizLessonDict = await _db.Quiz.Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id, x => x.LessonId);
        var sceneLessonDict = await _db.ScenarioScene.Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id, x => x.LessonId);

        int? OwnerLesson(PathItem item) => item.ItemType switch
        {
            PathItemType.Quiz => item.QuizId is int q ? quizLessonDict.GetValueOrDefault(q) : null,
            PathItemType.Simulation => item.ScenarioSceneId is int s ? sceneLessonDict.GetValueOrDefault(s) : null,
            _ => null,
        };

        var order = 0;
        var moduleCount = 0;
        var leafCount = 0;
        PathItem NewItem(PathItem source, PathItem? parent) => new()
        {
            LearningPathId = path.Id,
            ItemType = source.ItemType,
            LessonId = source.LessonId,
            QuizId = source.QuizId,
            ScenarioSceneId = source.ScenarioSceneId,
            ScormPackageId = source.ScormPackageId,
            ParentItem = parent,
            OrderIndex = ++order,
            IsRequired = true,
        };

        for (var index = 0; index < defaultList.Count; index++)
        {
            var item = defaultList[index];
            var childList = new List<PathItem>();
            if (item.ItemType == PathItemType.Lesson)
                while (index + 1 < defaultList.Count && OwnerLesson(defaultList[index + 1]) == item.LessonId)
                    childList.Add(defaultList[++index]);

            if (childList.Count == 0)
            {
                _db.PathItem.Add(NewItem(item, null));
                leafCount++;
                continue;
            }

            var module = new PathItem
            {
                LearningPathId = path.Id,
                ItemType = PathItemType.Module,
                Title = TrimTo(lessonTitleDict.GetValueOrDefault(item.LessonId!.Value) ?? ModuleDefaultTitle, 250),
                OrderIndex = ++order,
                IsRequired = true,
            };
            _db.PathItem.Add(module);
            moduleCount++;
            _db.PathItem.Add(NewItem(item, module));
            foreach (var child in childList) _db.PathItem.Add(NewItem(child, module));
            leafCount += 1 + childList.Count;
        }

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse(moduleCount > 0
            ? $"Đã dựng cấu trúc gồm {moduleCount} chương, {leafCount} mục nội dung."
            : $"Đã dựng cấu trúc gồm {leafCount} mục nội dung."));
    }

    // ==================== MỤC ====================

    public async Task<Result> AddItem(int pathId, PathItemRequest request)
    {
        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.Id == pathId);
        if (path is null) return Result.NotFound(new MessageResponse("Không tìm thấy lộ trình."));

        if (request.StartDate is not null && request.EndDate is not null && request.StartDate > request.EndDate)
            return Result.Invalid(new MessageResponse("Ngày bắt đầu phải trước ngày kết thúc."));
        if (request.ParentItemId is int parentId && !path.PathItemList.Any(x => x.Id == parentId && x.ItemType == PathItemType.Module))
            return Result.Invalid(new MessageResponse("Chương được chọn không thuộc khóa học này."));

        var item = new PathItem
        {
            LearningPathId = pathId,
            ItemType = request.ItemType,
            ParentItemId = request.ParentItemId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : TrimTo(request.Title.Trim(), 250),
            IsRequired = request.IsRequired,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
        };

        if (request.ItemType == PathItemType.Module)
        {
            if (item.Title is null) return Result.Invalid(new MessageResponse("Vui lòng nhập tên chương."));
        }
        else
        {
            var error = await BindResourceAsync(path, item, request.ItemType,
                request.LessonId ?? request.QuizId ?? request.ScenarioSceneId ?? request.ScormPackageId);
            if (error is not null) return error;
        }

        var ordered = PreorderOf(path.PathItemList);
        ordered.Insert(EndOfChildren(ordered, request.ParentItemId), item);
        await SaveOrderAsync(path, ordered);

        return Result.Success(new MessageResponse(request.ItemType == PathItemType.Module
            ? $"Đã thêm chương \"{item.Title}\"." : "Đã thêm mục vào cấu trúc khóa học.", item.Id));
    }

    /// <summary>Gắn tài nguyên vào mục mới, kiểm tra thuộc đúng khóa học và chưa có trong cấu trúc (một tài nguyên chỉ
    /// được là một hoạt động — định danh hoạt động SCORM lấy theo tài nguyên).</summary>
    private async Task<Result?> BindResourceAsync(LearningPath path, PathItem item, PathItemType type, int? resourceId)
    {
        if (resourceId is not int id) return Result.Invalid(new MessageResponse("Chưa chọn nội dung cần thêm."));
        var courseId = path.CourseId;
        bool exists;
        switch (type)
        {
            case PathItemType.Lesson:
                exists = await _db.Lesson.AnyAsync(x => x.Id == id && x.CourseId == courseId);
                item.LessonId = id;
                break;
            case PathItemType.Quiz:
                exists = await _db.Quiz.AnyAsync(x => x.Id == id && x.CourseId == courseId);
                item.QuizId = id;
                break;
            case PathItemType.Simulation:
                exists = await _db.ScenarioScene.AnyAsync(x => x.Id == id && x.CourseId == courseId);
                item.ScenarioSceneId = id;
                break;
            case PathItemType.Scorm:
                exists = await _db.ScormPackage.AnyAsync(x => x.Id == id && x.CourseId == courseId && x.Kind == ScormPackageKind.Imported);
                item.ScormPackageId = id;
                break;
            default:
                return Result.Invalid(new MessageResponse("Loại mục không hợp lệ."));
        }
        if (!exists) return Result.Invalid(new MessageResponse("Nội dung được chọn không thuộc khóa học này."));
        if (path.PathItemList.Any(x => x.ItemType == type && SameResource(x, item)))
            return Result.Conflict(new MessageResponse("Nội dung này đã có trong cấu trúc khóa học."));
        return null;
    }

    private static bool SameResource(PathItem a, PathItem b) => a.ItemType switch
    {
        PathItemType.Lesson => a.LessonId == b.LessonId,
        PathItemType.Quiz => a.QuizId == b.QuizId,
        PathItemType.Simulation => a.ScenarioSceneId == b.ScenarioSceneId,
        PathItemType.Scorm => a.ScormPackageId == b.ScormPackageId,
        _ => false,
    };

    public async Task<Result> UpdateItem(int itemId, PathItemUpdateRequest request)
    {
        var item = await _db.PathItem.FirstOrDefaultAsync(x => x.Id == itemId);
        if (item is null) return Result.NotFound(new MessageResponse("Không tìm thấy mục trong cấu trúc khóa học."));
        if (request.StartDate is not null && request.EndDate is not null && request.StartDate > request.EndDate)
            return Result.Invalid(new MessageResponse("Ngày bắt đầu phải trước ngày kết thúc."));

        var title = string.IsNullOrWhiteSpace(request.Title) ? null : TrimTo(request.Title.Trim(), 250);
        if (item.ItemType == PathItemType.Module && title is null)
            return Result.Invalid(new MessageResponse("Vui lòng nhập tên chương."));

        item.Title = title;
        item.IsRequired = request.IsRequired;
        item.IsVisible = request.IsVisible;
        item.StartDate = request.StartDate;
        item.EndDate = request.EndDate;
        item.AttemptLimit = request.AttemptLimit is > 0 ? request.AttemptLimit : null;
        // Ngưỡng hoàn thành theo tiến độ chỉ áp cho gói nhập ngoài (xem CourseOrganizationBuilder).
        item.CompletionThreshold = item.ItemType == PathItemType.Scorm && request.CompletionThreshold is > 0
            ? Math.Round(request.CompletionThreshold.Value, 4) : null;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã lưu thiết lập của mục."));
    }

    public async Task<Result> UpdateItemSchedule(int itemId, PathItemScheduleRequest request)
    {
        var item = await _db.PathItem.FirstOrDefaultAsync(x => x.Id == itemId);
        if (item is null) return Result.NotFound(new MessageResponse("Không tìm thấy bước trong lộ trình."));

        if (request.StartDate is not null && request.EndDate is not null && request.StartDate > request.EndDate)
            return Result.Invalid(new MessageResponse("Ngày bắt đầu phải trước ngày kết thúc."));

        item.StartDate = request.StartDate;
        item.EndDate = request.EndDate;
        item.IsRequired = request.IsRequired;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã cập nhật lịch cho bước này."));
    }

    /// <summary>Kéo thả: nhận lại TOÀN BỘ cây theo thứ tự mới. Chương cha phải đứng trước mục con trong danh sách —
    /// điều đó đồng thời chặn vòng lặp cha-con.</summary>
    public async Task<Result> MoveTree(int pathId, List<PathItemPlacement> placementList)
    {
        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.Id == pathId);
        if (path is null) return Result.NotFound(new MessageResponse("Không tìm thấy lộ trình."));

        var itemById = path.PathItemList.ToDictionary(x => x.Id);
        if (placementList.Count != itemById.Count || placementList.Select(x => x.Id).Distinct().Count() != itemById.Count
            || placementList.Any(x => !itemById.ContainsKey(x.Id)))
            return Result.Invalid(new MessageResponse("Cấu trúc đã thay đổi ở nơi khác — tải lại trang rồi thử lại."));

        var seen = new HashSet<int>();
        foreach (var placement in placementList)
        {
            if (placement.ParentItemId is int parentId
                && (!seen.Contains(parentId) || itemById[parentId].ItemType != PathItemType.Module))
                return Result.Invalid(new MessageResponse("Chỉ được đặt mục vào trong một chương."));
            seen.Add(placement.Id);
        }

        foreach (var placement in placementList) itemById[placement.Id].ParentItemId = placement.ParentItemId;
        await SaveOrderAsync(path, placementList.Select(x => itemById[x.Id]).ToList());
        return Result.Success(new MessageResponse("Đã lưu thứ tự cấu trúc khóa học."));
    }

    public async Task<Result> ReorderItem(List<ReorderItem> itemList)
    {
        var idList = itemList.Select(x => x.Id).ToList();
        var pathItemList = await _db.PathItem.Where(x => idList.Contains(x.Id)).ToListAsync();
        if (pathItemList.Count == 0) return Result.Success(new MessageResponse("Không có gì thay đổi."));

        var pathId = pathItemList[0].LearningPathId;
        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstAsync(x => x.Id == pathId);
        var wanted = itemList.ToDictionary(x => x.Id, x => x.OrderIndex);
        var ordered = path.PathItemList
            .OrderBy(x => wanted.TryGetValue(x.Id, out var index) ? index : x.OrderIndex).ThenBy(x => x.Id).ToList();
        await SaveOrderAsync(path, ordered);
        return Result.Success(new MessageResponse("Đã cập nhật thứ tự lộ trình."));
    }

    /// <summary>Xóa một mục. Xóa chương thì các mục bên trong KHÔNG mất theo — chúng lên thế chỗ chương, đúng vị trí cũ.</summary>
    public async Task<Result> DeleteItem(int itemId)
    {
        var item = await _db.PathItem.FirstOrDefaultAsync(x => x.Id == itemId);
        if (item is null) return Result.NotFound(new MessageResponse("Không tìm thấy mục trong cấu trúc khóa học."));

        if (item.ItemType == PathItemType.Module)
        {
            var childList = await _db.PathItem.Where(x => x.ParentItemId == item.Id).ToListAsync();
            foreach (var child in childList) child.ParentItemId = item.ParentItemId;
            await _db.SaveChangesAsync();
        }

        _db.PathItem.Remove(item);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse(item.ItemType == PathItemType.Module
            ? "Đã xóa chương — các mục bên trong được giữ lại." : "Đã gỡ mục khỏi cấu trúc khóa học."));
    }

    // ==================== TÀI NGUYÊN MỚI TỰ VÀO CẤU TRÚC ====================

    /// <summary>
    /// Tài nguyên vừa tạo (bài học, bài kiểm tra, mô phỏng, gói SCORM) tự trở thành một mục của cấu trúc — soạn nội
    /// dung là đang soạn khóa học SCORM, không có bước "thêm vào lộ trình" riêng.
    ///
    /// Bài kiểm tra / mô phỏng gắn với một bài học đứng ngay sau bài học đó (sau các mục cùng gắn bài đó đã có); thi
    /// cuối khóa xuống cuối; nội dung khác vào cuối khóa học nhưng trước bài thi cuối khóa.
    /// </summary>
    public async Task AppendResourceAsync(int courseId, PathItemType type, int resourceId, int? parentItemId = null, bool isRequired = true)
    {
        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.CourseId == courseId);
        if (path is null || path.PathItemList.Count == 0)
        {
            // Chưa từng có cấu trúc: dựng cấu trúc mặc định — đã gồm luôn tài nguyên vừa tạo (nếu nó giao được).
            path = await EnsurePathAsync(courseId);
            if (path.PathItemList.Any(x => x.ItemType == type && SameResource(x, Probe(type, resourceId)))) return;
        }

        var item = Probe(type, resourceId);
        if (path.PathItemList.Any(x => x.ItemType == type && SameResource(x, item))) return;
        item.LearningPathId = path.Id;
        item.IsRequired = isRequired;

        var ordered = PreorderOf(path.PathItemList);
        if (parentItemId is int parentId && path.PathItemList.Any(x => x.Id == parentId && x.ItemType == PathItemType.Module))
        {
            item.ParentItemId = parentId;
            ordered.Insert(EndOfChildren(ordered, parentId), item);
            await SaveOrderAsync(path, ordered);
            return;
        }

        int? ownerLessonId = null;
        var isFinalExam = false;
        if (type == PathItemType.Quiz)
        {
            var quiz = await _db.Quiz.AsNoTracking().Where(x => x.Id == resourceId).Select(x => new { x.LessonId, x.IsFinalExam }).FirstOrDefaultAsync();
            ownerLessonId = quiz?.LessonId;
            isFinalExam = quiz?.IsFinalExam ?? false;
        }
        else if (type == PathItemType.Simulation)
            ownerLessonId = await _db.ScenarioScene.AsNoTracking().Where(x => x.Id == resourceId).Select(x => x.LessonId).FirstOrDefaultAsync();

        var position = -1;
        var lessonItem = ownerLessonId is int lessonId
            ? ordered.FirstOrDefault(x => x.ItemType == PathItemType.Lesson && x.LessonId == lessonId) : null;
        if (lessonItem is not null)
        {
            item.ParentItemId = lessonItem.ParentItemId;
            position = await PositionAfterLessonAsync(ordered, lessonItem, courseId);
        }
        else if (!isFinalExam)
        {
            // Trước bài thi cuối khóa đầu tiên đứng ở gốc (nếu có), không thì cuối khóa học.
            var finalIdSet = (await _db.Quiz.AsNoTracking().Where(x => x.CourseId == courseId && x.IsFinalExam).Select(x => x.Id).ToListAsync()).ToHashSet();
            var firstFinal = ordered.FindIndex(x => x.ParentItemId is null && x.ItemType == PathItemType.Quiz && x.QuizId is int q && finalIdSet.Contains(q));
            if (firstFinal >= 0) position = firstFinal;
        }

        ordered.Insert(position >= 0 ? position : ordered.Count, item);
        await SaveOrderAsync(path, ordered);
    }

    /// <summary>
    /// Bài kiểm tra / mô phỏng vừa được SỬA để gắn vào một bài học: đưa mục của nó trong cấu trúc về ngay sau bài học
    /// đó (cùng chương, sau các mục đã gắn cùng bài học) — cùng quy tắc với lúc tạo mới (<see cref="AppendResourceAsync"/>).
    /// Không có bước này, đổi "Bài học gắn kèm" chỉ đổi cột LessonId còn cấu trúc đứng yên, học viên và người soạn
    /// thấy bài kiểm tra vẫn nằm ngoài chương của bài học mà nó đi kèm.
    ///
    /// Chỉ di chuyển khi thật sự cần: bài học gắn kèm vừa đổi, hoặc mục đang nằm khác chương với bài học của nó. Mục
    /// đã ở đúng chương thì giữ nguyên thứ tự người soạn đã xếp tay — sửa tên hay thời gian làm bài không được làm
    /// nó nhảy chỗ. Gỡ khỏi bài học (LessonId = null), chưa có cấu trúc, mục chưa nằm trong cấu trúc hay bài học chưa
    /// có trong cấu trúc đều giữ nguyên: để người soạn tự quyết.
    /// </summary>
    public async Task AlignAttachedResourceAsync(int courseId, PathItemType type, int resourceId, bool lessonChanged)
    {
        if (type is not (PathItemType.Quiz or PathItemType.Simulation)) return;

        var path = await _db.LearningPath.Include(x => x.PathItemList).FirstOrDefaultAsync(x => x.CourseId == courseId);
        if (path is null || path.PathItemList.Count == 0) return;   // chưa dựng cấu trúc: thứ tự mặc định tự tính theo LessonId

        var probe = Probe(type, resourceId);
        var item = path.PathItemList.FirstOrDefault(x => x.ItemType == type && SameResource(x, probe));
        if (item is null) return;

        var ownerLessonId = type == PathItemType.Quiz
            ? await _db.Quiz.AsNoTracking().Where(x => x.Id == resourceId).Select(x => x.LessonId).FirstOrDefaultAsync()
            : await _db.ScenarioScene.AsNoTracking().Where(x => x.Id == resourceId).Select(x => x.LessonId).FirstOrDefaultAsync();
        if (ownerLessonId is not int lessonId) return;

        var ordered = PreorderOf(path.PathItemList);
        var lessonItem = ordered.FirstOrDefault(x => x.ItemType == PathItemType.Lesson && x.LessonId == lessonId);
        if (lessonItem is null) return;
        if (!lessonChanged && item.ParentItemId == lessonItem.ParentItemId) return;

        var before = ordered.Select(x => (x.Id, x.ParentItemId)).ToList();
        ordered.Remove(item);
        item.ParentItemId = lessonItem.ParentItemId;
        ordered.Insert(await PositionAfterLessonAsync(ordered, lessonItem, courseId), item);

        // Đã đứng đúng chỗ rồi (VD gắn lại đúng bài học cũ): khỏi ghi lại số thứ tự của cả cây.
        if (before.SequenceEqual(ordered.Select(x => (x.Id, x.ParentItemId)))) return;
        await SaveOrderAsync(path, ordered);
    }

    /// <summary>Vị trí chèn để một bài kiểm tra / mô phỏng gắn với <paramref name="lessonItem"/> đứng ngay sau bài học và
    /// các mục đã gắn cùng bài học đứng liền sau nó (cùng cấp). <paramref name="ordered"/> không được chứa mục sắp chèn.</summary>
    private async Task<int> PositionAfterLessonAsync(List<PathItem> ordered, PathItem lessonItem, int courseId)
    {
        var lessonId = lessonItem.LessonId;
        var quizLessonDict = await _db.Quiz.AsNoTracking().Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id, x => x.LessonId);
        var sceneLessonDict = await _db.ScenarioScene.AsNoTracking().Where(x => x.CourseId == courseId).ToDictionaryAsync(x => x.Id, x => x.LessonId);
        bool OwnedBySameLesson(PathItem x) => x.ParentItemId == lessonItem.ParentItemId && (
            (x.ItemType == PathItemType.Quiz && x.QuizId is int q && quizLessonDict.GetValueOrDefault(q) == lessonId)
            || (x.ItemType == PathItemType.Simulation && x.ScenarioSceneId is int s && sceneLessonDict.GetValueOrDefault(s) == lessonId));

        var end = SubtreeEnd(ordered, ordered.IndexOf(lessonItem));
        while (end + 1 < ordered.Count && OwnedBySameLesson(ordered[end + 1])) end = SubtreeEnd(ordered, end + 1);
        return end + 1;
    }

    private static PathItem Probe(PathItemType type, int resourceId) => new()
    {
        ItemType = type,
        LessonId = type == PathItemType.Lesson ? resourceId : null,
        QuizId = type == PathItemType.Quiz ? resourceId : null,
        ScenarioSceneId = type == PathItemType.Simulation ? resourceId : null,
        ScormPackageId = type == PathItemType.Scorm ? resourceId : null,
        IsRequired = true,
    };

    // ==================== THỨ TỰ CÂY ====================

    private static List<PathItem> PreorderOf(IEnumerable<PathItem> itemList)
        => OrganizationOrder.Preorder(OrganizationOrder.BuildTree(itemList)).Select(n => n.Item).ToList();

    /// <summary>Vị trí (trong danh sách tiền thứ tự) của phần tử cuối cùng thuộc nhánh bắt đầu tại <paramref name="index"/>.</summary>
    private static int SubtreeEnd(List<PathItem> ordered, int index)
    {
        var root = ordered[index];
        var memberIdSet = new HashSet<int> { root.Id };
        var end = index;
        while (end + 1 < ordered.Count && ordered[end + 1].ParentItemId is int parent && memberIdSet.Contains(parent))
        {
            end++;
            memberIdSet.Add(ordered[end].Id);
        }
        return end;
    }

    /// <summary>Vị trí chèn để mục mới đứng CUỐI chương <paramref name="parentId"/> (null = cuối khóa học).</summary>
    private static int EndOfChildren(List<PathItem> ordered, int? parentId)
    {
        if (parentId is not int id) return ordered.Count;
        var index = ordered.FindIndex(x => x.Id == id);
        return index < 0 ? ordered.Count : SubtreeEnd(ordered, index) + 1;
    }

    /// <summary>
    /// Đánh số lại OrderIndex = 1..n theo thứ tự đã cho (mục mới có Id = 0 được thêm vào ở bước này).
    ///
    /// Hai lượt ghi: cột có chỉ mục duy nhất (LearningPathId, OrderIndex), đổi chỗ hai mục trong một lượt là có lúc
    /// hai dòng cùng mang một số. Lượt một dồn mọi mục cũ xuống vùng số âm chưa ai dùng, lượt hai mới đặt số thật.
    /// </summary>
    private async Task SaveOrderAsync(LearningPath path, List<PathItem> ordered)
    {
        var existingList = ordered.Where(x => x.Id != 0).ToList();
        if (existingList.Count > 0)
        {
            var floor = Math.Min(0, existingList.Min(x => x.OrderIndex)) - 1;
            for (var i = 0; i < existingList.Count; i++) existingList[i].OrderIndex = floor - i;
            await _db.SaveChangesAsync();
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            item.OrderIndex = i + 1;
            if (item.Id == 0)
            {
                item.LearningPathId = path.Id;
                _db.PathItem.Add(item);
            }
        }
        await _db.SaveChangesAsync();
    }

    private static string TrimTo(string value, int length) => value.Length <= length ? value : value[..length];
}
