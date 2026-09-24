// File: Application/Features/LearningPaths/LearningPathService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.LearningPaths;

public class LearningPathService : ILearningPathService
{
    private readonly IAppDbContext _db;

    public LearningPathService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result> GetByCourse(int courseId)
    {
        var path = await _db.LearningPath.AsNoTracking()
            .Include(x => x.Course)
            .Include(x => x.PathItemList).ThenInclude(i => i.Lesson)
            .Include(x => x.PathItemList).ThenInclude(i => i.Quiz)
            .Include(x => x.PathItemList).ThenInclude(i => i.ScenarioScene)
            .FirstOrDefaultAsync(x => x.CourseId == courseId);

        if (path is null) return Result.NotFound(new MessageResponse("Khóa học chưa có lộ trình học tập."));

        var itemList = path.PathItemList.OrderBy(x => x.OrderIndex).Select(i => new PathItemDto(
            i.Id, i.ItemType.ToString(), i.LessonId, i.QuizId,
            i.Lesson?.Title ?? i.Quiz?.Title ?? i.ScenarioScene?.Title ?? "(đã xóa)",
            i.Lesson?.DurationMinutes ?? i.Quiz?.TimeLimitMinutes ?? 0,
            i.OrderIndex, i.IsRequired, i.StartDate, i.EndDate, i.Quiz?.LessonId, i.ScenarioSceneId)).ToList();

        return Result.Success(new LearningPathDto(path.Id, path.CourseId, path.Course.Title,
            path.Name, path.Description, path.IsSequential, itemList));
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

        return Result.Success(new MessageResponse($"Đã tạo lộ trình #{path.Id}."));
    }

    public async Task<Result> Update(int id, LearningPathRequest request)
    {
        var path = await _db.LearningPath.FirstOrDefaultAsync(x => x.Id == id);
        if (path is null) return Result.NotFound(new MessageResponse("Không tìm thấy lộ trình."));

        path.Name = request.Name.Trim();
        path.Description = request.Description;
        path.IsSequential = request.IsSequential;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật lộ trình thành công."));
    }

    public async Task<Result> AutoGenerate(int courseId)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var path = await _db.LearningPath.Include(x => x.PathItemList)
            .FirstOrDefaultAsync(x => x.CourseId == courseId);

        if (path is null)
        {
            path = new LearningPath { CourseId = courseId, Name = $"Lộ trình {course.Title}", IsSequential = true };
            _db.LearningPath.Add(path);
            await _db.SaveChangesAsync();
        }
        else
        {
            _db.PathItem.RemoveRange(path.PathItemList);
            await _db.SaveChangesAsync();
        }

        var lessonList = await _db.Lesson.Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.OrderIndex).ToListAsync();
        var quizList = await _db.Quiz.Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.IsFinalExam).ThenBy(x => x.Id).ToListAsync();

        // Mô phỏng 3D CHƯA CÓ TÌNH HUỐNG NÀO thì bỏ qua: MaxScore của nó bằng 0 nên học viên không bao giờ
        // đạt được, mà lộ trình lại tuần tự — đưa vào thì chặn đứng cả khóa học ở đúng bước đó.
        var scenarioList = await _db.ScenarioScene
            .Where(x => x.CourseId == courseId && x.IsActive && x.NodeList.Count > 0)
            .OrderBy(x => x.Id).ToListAsync();

        // Thứ tự trong một khối: BÀI HỌC -> MÔ PHỎNG (thực hành) -> BÀI KIỂM TRA (đánh giá). Cả mô phỏng và
        // bài kiểm tra gắn theo bài học đều phải nằm ngay sau bài học đó thì lộ trình tuần tự mới buộc học
        // viên làm xong mới được mở bài kế tiếp. Loại không gắn bài học nào (VD thi cuối khóa, mô phỏng độc
        // lập) xếp sau cùng.
        var order = 1;
        foreach (var lesson in lessonList)
        {
            _db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Lesson, LessonId = lesson.Id, OrderIndex = order++, IsRequired = true });
            foreach (var childScenario in scenarioList.Where(s => s.LessonId == lesson.Id))
                _db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Simulation, ScenarioSceneId = childScenario.Id, OrderIndex = order++, IsRequired = true });
            foreach (var childQuiz in quizList.Where(q => q.LessonId == lesson.Id))
                _db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Quiz, QuizId = childQuiz.Id, OrderIndex = order++, IsRequired = true });
        }
        foreach (var standaloneQuiz in quizList.Where(q => q.LessonId is null))
            _db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Quiz, QuizId = standaloneQuiz.Id, OrderIndex = order++, IsRequired = true });
        foreach (var standaloneScenario in scenarioList.Where(s => s.LessonId is null))
            _db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Simulation, ScenarioSceneId = standaloneScenario.Id, OrderIndex = order++, IsRequired = true });

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã sinh lộ trình gồm {order - 1} bước."));
    }

    public async Task<Result> AddItem(int pathId, PathItemRequest request)
    {
        if (!await _db.LearningPath.AnyAsync(x => x.Id == pathId))
            return Result.NotFound(new MessageResponse("Không tìm thấy lộ trình."));

        if (request.ItemType == PathItemType.Lesson && request.LessonId is null)
            return Result.Invalid(new MessageResponse("Thiếu LessonId."));
        if (request.ItemType == PathItemType.Quiz && request.QuizId is null)
            return Result.Invalid(new MessageResponse("Thiếu QuizId."));
        if (request.ItemType == PathItemType.Simulation && request.ScenarioSceneId is null)
            return Result.Invalid(new MessageResponse("Thiếu ScenarioSceneId."));

        var maxOrder = await _db.PathItem.Where(x => x.LearningPathId == pathId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        _db.PathItem.Add(new PathItem
        {
            LearningPathId = pathId,
            ItemType = request.ItemType,
            LessonId = request.ItemType == PathItemType.Lesson ? request.LessonId : null,
            QuizId = request.ItemType == PathItemType.Quiz ? request.QuizId : null,
            ScenarioSceneId = request.ItemType == PathItemType.Simulation ? request.ScenarioSceneId : null,
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            IsRequired = request.IsRequired,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        });

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã thêm bước vào lộ trình."));
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

    public async Task<Result> ReorderItem(List<ReorderItem> itemList)
    {
        var idList = itemList.Select(x => x.Id).ToList();
        var pathItemList = await _db.PathItem.Where(x => idList.Contains(x.Id)).ToListAsync();

        foreach (var item in pathItemList)
            item.OrderIndex = itemList.First(x => x.Id == item.Id).OrderIndex;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã cập nhật thứ tự lộ trình."));
    }

    public async Task<Result> DeleteItem(int itemId)
    {
        var item = await _db.PathItem.FirstOrDefaultAsync(x => x.Id == itemId);
        if (item is null) return Result.NotFound(new MessageResponse("Không tìm thấy bước trong lộ trình."));

        _db.PathItem.Remove(item);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa bước khỏi lộ trình."));
    }
}
