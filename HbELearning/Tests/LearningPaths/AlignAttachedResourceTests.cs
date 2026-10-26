using HbELearning.Application.DTOs;
using HbELearning.Application.Features.LearningPaths;
using HbELearning.Application.Features.Quizzes;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Tests.LearningPaths;

/// <summary>
/// Sửa "Bài học gắn kèm" của một bài kiểm tra / mô phỏng thì mục của nó trong cấu trúc khóa học phải đi theo bài học đó.
/// Lúc TẠO, AppendResourceAsync đã đặt đúng chỗ; lúc SỬA trước đây chỉ đổi cột LessonId còn cấu trúc đứng yên, nên bài kiểm
/// tra vẫn nằm ngoài chương của bài học nó đi kèm.
///
/// Cấu trúc dựng sẵn (số trong ngoặc là thứ tự):
///   Chương 1 (1)  ├ Bài 1 (2)  └ KT Bài 1 (3, gắn Bài 1)
///   Chương 2 (4)  └ Bài 2 (5)
///   KT rời (6)  — không gắn bài nào, đứng ở gốc
///   Thi cuối khóa (7)
/// </summary>
public class AlignAttachedResourceTests
{
    private sealed class Fixture
    {
        public required ClassRoomWorld World { get; init; }
        public required int CourseId { get; init; }
        public required Dictionary<string, int> LessonId { get; init; }
        public required Dictionary<string, int> QuizId { get; init; }
    }

    private static Fixture Build(bool withPath = true)
    {
        var world = ClassRoomWorld.Create();
        using var db = world.NewDb();

        var course = new Course
        {
            Code = "KH-PATH", Title = "Khóa thử cấu trúc", CreatedById = world.Host.Id,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
        };
        db.Course.Add(course);
        db.SaveChanges();

        var lesson1 = new Lesson { CourseId = course.Id, Title = "Bài 1", OrderIndex = 1 };
        var lesson2 = new Lesson { CourseId = course.Id, Title = "Bài 2", OrderIndex = 2 };
        db.Lesson.AddRange(lesson1, lesson2);
        db.SaveChanges();

        var quiz1 = new Quiz { CourseId = course.Id, Title = "KT Bài 1", LessonId = lesson1.Id };
        var loose = new Quiz { CourseId = course.Id, Title = "KT rời" };
        var final = new Quiz { CourseId = course.Id, Title = "Thi cuối khóa", IsFinalExam = true };
        db.Quiz.AddRange(quiz1, loose, final);
        db.SaveChanges();

        if (withPath)
        {
            var path = new LearningPath { CourseId = course.Id, Name = "Lộ trình" };
            db.LearningPath.Add(path);
            db.SaveChanges();

            var chapter1 = new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Module, Title = "Chương 1", OrderIndex = 1 };
            var chapter2 = new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Module, Title = "Chương 2", OrderIndex = 4 };
            db.PathItem.AddRange(chapter1, chapter2);
            db.SaveChanges();

            db.PathItem.AddRange(
                new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Lesson, LessonId = lesson1.Id, ParentItemId = chapter1.Id, OrderIndex = 2 },
                new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Quiz, QuizId = quiz1.Id, ParentItemId = chapter1.Id, OrderIndex = 3 },
                new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Lesson, LessonId = lesson2.Id, ParentItemId = chapter2.Id, OrderIndex = 5 },
                new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Quiz, QuizId = loose.Id, OrderIndex = 6 },
                new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Quiz, QuizId = final.Id, OrderIndex = 7 });
            db.SaveChanges();
        }

        return new Fixture
        {
            World = world, CourseId = course.Id,
            LessonId = new() { ["Bài 1"] = lesson1.Id, ["Bài 2"] = lesson2.Id },
            QuizId = new() { ["KT Bài 1"] = quiz1.Id, ["KT rời"] = loose.Id, ["Thi cuối khóa"] = final.Id },
        };
    }

    /// <summary>Cấu trúc đọc ra đúng thứ tự học viên thấy (tiền thứ tự của cây), mỗi dòng "Tên (chương cha)" — dễ so với thứ tự mong đợi.</summary>
    private static List<string> Layout(Fixture f)
    {
        using var db = f.World.NewDb();
        var itemList = OrganizationOrder.Preorder(OrganizationOrder.BuildTree(
            db.PathItem.Include(x => x.Lesson).Include(x => x.Quiz).Include(x => x.ScenarioScene)
                .Where(x => x.LearningPath.CourseId == f.CourseId).ToList())).Select(n => n.Item).ToList();
        string Name(PathItem x) => x.ItemType switch
        {
            PathItemType.Module => x.Title!,
            PathItemType.Lesson => x.Lesson!.Title,
            PathItemType.Quiz => x.Quiz!.Title,
            _ => x.ScenarioScene!.Title,
        };
        return itemList.Select(x => x.ParentItemId is int parent
            ? $"{Name(x)} ({Name(itemList.Single(p => p.Id == parent))})" : Name(x)).ToList();
    }

    private static QuizRequest Edit(Fixture f, string quiz, int? lessonId) => new()
    {
        CourseId = f.CourseId, LessonId = lessonId, Title = quiz, PassScore = 70, TimeLimitMinutes = 45, MaxAttempt = 3,
        IsFinalExam = quiz == "Thi cuối khóa",
    };

    [Fact]
    public async Task Attaching_a_loose_quiz_to_a_lesson_moves_it_into_that_lessons_chapter()
    {
        var f = Build();

        var result = await f.World.Service<IQuizService>().Update(f.QuizId["KT rời"], Edit(f, "KT rời", f.LessonId["Bài 2"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[]
        {
            "Chương 1", "Bài 1 (Chương 1)", "KT Bài 1 (Chương 1)",
            "Chương 2", "Bài 2 (Chương 2)", "KT rời (Chương 2)",
            "Thi cuối khóa",
        }, Layout(f));
    }

    [Fact]
    public async Task A_quiz_joins_the_other_items_already_attached_to_the_same_lesson()
    {
        var f = Build();

        await f.World.Service<IQuizService>().Update(f.QuizId["KT rời"], Edit(f, "KT rời", f.LessonId["Bài 1"]));

        // Đứng sau KT Bài 1 (đã gắn Bài 1), không chen vào giữa Bài 1 và KT Bài 1.
        Assert.Equal(new[]
        {
            "Chương 1", "Bài 1 (Chương 1)", "KT Bài 1 (Chương 1)", "KT rời (Chương 1)",
            "Chương 2", "Bài 2 (Chương 2)", "Thi cuối khóa",
        }, Layout(f));
    }

    [Fact]
    public async Task Switching_the_attached_lesson_moves_the_item_to_the_new_lesson()
    {
        var f = Build();

        await f.World.Service<IQuizService>().Update(f.QuizId["KT Bài 1"], Edit(f, "KT Bài 1", f.LessonId["Bài 2"]));

        Assert.Equal(new[]
        {
            "Chương 1", "Bài 1 (Chương 1)",
            "Chương 2", "Bài 2 (Chương 2)", "KT Bài 1 (Chương 2)",
            "KT rời", "Thi cuối khóa",
        }, Layout(f));
    }

    [Fact]
    public async Task A_quiz_already_attached_but_sitting_outside_its_chapter_is_fixed_by_saving_it_again()
    {
        // Đúng tình huống dữ liệu thật: LessonId đã gắn từ trước nhưng mục vẫn đứng ở gốc.
        var f = Build();
        using (var db = f.World.NewDb())
        {
            var loose = await db.Quiz.SingleAsync(q => q.Id == f.QuizId["KT rời"]);
            loose.LessonId = f.LessonId["Bài 2"];
            await db.SaveChangesAsync();
        }

        await f.World.Service<IQuizService>().Update(f.QuizId["KT rời"], Edit(f, "KT rời", f.LessonId["Bài 2"]));   // không đổi bài học

        Assert.Contains("KT rời (Chương 2)", Layout(f));
    }

    [Fact]
    public async Task Saving_without_changing_the_lesson_keeps_the_order_the_author_set_by_hand()
    {
        var f = Build();
        // Người soạn kéo KT Bài 1 xuống cuối Chương 1, sau một bài học khác cùng chương.
        using (var db = f.World.NewDb())
        {
            var path = await db.LearningPath.SingleAsync(p => p.CourseId == f.CourseId);
            var chapter1 = await db.PathItem.SingleAsync(x => x.LearningPathId == path.Id && x.Title == "Chương 1");
            var extra = new Lesson { CourseId = f.CourseId, Title = "Bài 1b", OrderIndex = 3 };
            db.Lesson.Add(extra);
            await db.SaveChangesAsync();
            db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Lesson, LessonId = extra.Id, ParentItemId = chapter1.Id, OrderIndex = 100 });
            var quizItem = await db.PathItem.SingleAsync(x => x.QuizId == f.QuizId["KT Bài 1"]);
            quizItem.OrderIndex = 101;
            await db.SaveChangesAsync();
        }
        var before = Layout(f);

        await f.World.Service<IQuizService>().Update(f.QuizId["KT Bài 1"], Edit(f, "KT Bài 1", f.LessonId["Bài 1"]));   // chỉ đổi thời gian làm bài

        var after = Layout(f);
        Assert.Equal(before, after);
        // Vẫn nằm sau Bài 1b, không nhảy về ngay sau Bài 1.
        Assert.True(after.IndexOf("KT Bài 1 (Chương 1)") > after.IndexOf("Bài 1b (Chương 1)"));
    }

    [Fact]
    public async Task Detaching_a_quiz_from_its_lesson_leaves_it_where_the_author_put_it()
    {
        var f = Build();
        var before = Layout(f);

        await f.World.Service<IQuizService>().Update(f.QuizId["KT Bài 1"], Edit(f, "KT Bài 1", null));

        Assert.Equal(before, Layout(f));
    }

    [Fact]
    public async Task A_quiz_that_is_not_in_the_structure_is_not_added_by_attaching_it()
    {
        var f = Build();
        using (var db = f.World.NewDb())
        {
            db.PathItem.Remove(await db.PathItem.SingleAsync(x => x.QuizId == f.QuizId["KT rời"]));
            await db.SaveChangesAsync();
        }
        var before = Layout(f);

        await f.World.Service<IQuizService>().Update(f.QuizId["KT rời"], Edit(f, "KT rời", f.LessonId["Bài 2"]));

        Assert.Equal(before, Layout(f));   // Chưa có trong cấu trúc = "chưa đưa vào" — người soạn tự quyết.
    }

    [Fact]
    public async Task A_course_without_a_saved_structure_gets_none_created()
    {
        var f = Build(withPath: false);

        var result = await f.World.Service<IQuizService>().Update(f.QuizId["KT rời"], Edit(f, "KT rời", f.LessonId["Bài 2"]));

        Assert.True(result.IsSuccess);
        using var db = f.World.NewDb();
        Assert.False(await db.LearningPath.AnyAsync(p => p.CourseId == f.CourseId));
    }

    [Fact]
    public async Task Attaching_a_simulation_to_a_lesson_moves_it_into_the_lessons_chapter()
    {
        var f = Build();
        int sceneId;
        using (var db = f.World.NewDb())
        {
            var path = await db.LearningPath.SingleAsync(p => p.CourseId == f.CourseId);
            var scene = new ScenarioScene { CourseId = f.CourseId, Title = "Mô phỏng thử", LessonId = f.LessonId["Bài 2"] };
            db.ScenarioScene.Add(scene);
            await db.SaveChangesAsync();
            db.PathItem.Add(new PathItem { LearningPathId = path.Id, ItemType = PathItemType.Simulation, ScenarioSceneId = scene.Id, OrderIndex = 8 });
            await db.SaveChangesAsync();
            sceneId = scene.Id;
        }

        await f.World.Service<ILearningPathService>().AlignAttachedResourceAsync(f.CourseId, PathItemType.Simulation, sceneId, lessonChanged: true);

        Assert.Contains("Mô phỏng thử (Chương 2)", Layout(f));
        Assert.Equal("Bài 2 (Chương 2)", Layout(f)[Layout(f).IndexOf("Mô phỏng thử (Chương 2)") - 1]);
    }
}
