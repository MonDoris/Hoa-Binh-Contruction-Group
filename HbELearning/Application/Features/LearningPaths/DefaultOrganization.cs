// File: Application/Features/LearningPaths/DefaultOrganization.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.LearningPaths;

/// <summary>
/// Cấu trúc khóa học MẶC ĐỊNH khi người soạn chưa tự dựng: bài học → mô phỏng 3D (thực hành) → bài kiểm tra (đánh
/// giá) của chính bài đó; gói SCORM nhập ngoài sau các bài học; bài kiểm tra và mô phỏng không gắn bài nào (thi cuối
/// khóa...) đứng cuối.
///
/// Một quy tắc duy nhất cho ba chỗ: "Tự sinh cấu trúc" (ghi xuống database), lộ trình của khóa học cũ chưa từng có
/// cấu trúc, và cây SCORM dựng cho khóa học đó — lệch nhau là học viên thấy một thứ tự, trình phát chạy thứ tự khác.
/// </summary>
public static class DefaultOrganization
{
    /// <summary>Danh sách mục PHẲNG theo đúng thứ tự học, chưa lưu (Id âm, chỉ để dựng cây trong bộ nhớ).</summary>
    public static async Task<List<PathItem>> BuildItemListAsync(IAppDbContext db, int courseId)
    {
        var lessonList = await db.Lesson.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id).ToListAsync();
        var quizList = await db.Quiz.AsNoTracking().Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.IsFinalExam).ThenBy(x => x.Id).ToListAsync();
        // Mô phỏng CHƯA CÓ TÌNH HUỐNG NÀO thì bỏ qua: MaxScore của nó bằng 0 nên học viên không bao giờ đạt được, đưa
        // vào khóa học tuần tự là chặn đứng học viên ở đúng mục đó.
        var sceneList = await db.ScenarioScene.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive && x.NodeList.Count > 0)
            .OrderBy(x => x.Id).ToListAsync();
        var packageList = await db.ScormPackage.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.Kind == ScormPackageKind.Imported)
            .OrderBy(x => x.Id).ToListAsync();

        var list = new List<PathItem>();
        void Add(PathItemType type, int? lessonId = null, int? quizId = null, int? sceneId = null, int? packageId = null)
            => list.Add(new PathItem
            {
                Id = -(list.Count + 1),
                ItemType = type,
                LessonId = lessonId,
                QuizId = quizId,
                ScenarioSceneId = sceneId,
                ScormPackageId = packageId,
                OrderIndex = list.Count + 1,
                IsRequired = true,
                IsVisible = true,
            });

        foreach (var lesson in lessonList)
        {
            Add(PathItemType.Lesson, lessonId: lesson.Id);
            foreach (var scene in sceneList.Where(s => s.LessonId == lesson.Id)) Add(PathItemType.Simulation, sceneId: scene.Id);
            foreach (var quiz in quizList.Where(q => q.LessonId == lesson.Id)) Add(PathItemType.Quiz, quizId: quiz.Id);
        }
        foreach (var package in packageList) Add(PathItemType.Scorm, packageId: package.Id);
        foreach (var quiz in quizList.Where(q => q.LessonId is null)) Add(PathItemType.Quiz, quizId: quiz.Id);
        foreach (var scene in sceneList.Where(s => s.LessonId is null)) Add(PathItemType.Simulation, sceneId: scene.Id);
        return list;
    }

    /// <summary>Các mục của khóa học theo thứ tự cây: cấu trúc người soạn đã dựng, hoặc cấu trúc mặc định nếu chưa có.</summary>
    public static async Task<(LearningPath? Path, List<PathItem> ItemList)> LoadAsync(IAppDbContext db, int courseId)
    {
        var path = await db.LearningPath.AsNoTracking().Include(x => x.PathItemList)
            .FirstOrDefaultAsync(x => x.CourseId == courseId);
        if (path is not null && path.PathItemList.Count > 0) return (path, path.PathItemList.ToList());
        return (path, await BuildItemListAsync(db, courseId));
    }
}
