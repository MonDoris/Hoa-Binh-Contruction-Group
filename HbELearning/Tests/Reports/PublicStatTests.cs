using HbELearning.Application.DTOs;
using HbELearning.Application.Features.Reports;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.Reports;

/// <summary>
/// Số liệu công khai trên trang chủ (/report/public-stat) chỉ đếm nội dung ĐANG HIỆN HÀNH: bài học / bài kiểm tra của
/// khóa còn là bản nháp, đang chờ duyệt, bị từ chối hay đã lưu trữ thì chưa ai học được nên không được lên trang chủ.
/// </summary>
public class PublicStatTests
{
    private static Course AddCourse(ClassRoomWorld world, string code, CourseStatus status, int lessons, int quizzes, int hiddenQuizzes = 0)
    {
        using var db = world.NewDb();
        var course = new Course
        {
            Code = code, Title = "Khóa " + code, Status = status, CreatedById = world.Host.Id,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
        };
        db.Course.Add(course);
        db.SaveChanges();

        for (var i = 0; i < lessons; i++) db.Lesson.Add(new Lesson { CourseId = course.Id, Title = $"Bài {i + 1}", OrderIndex = i + 1 });
        for (var i = 0; i < quizzes; i++) db.Quiz.Add(new Quiz { CourseId = course.Id, Title = $"Kiểm tra {i + 1}" });
        for (var i = 0; i < hiddenQuizzes; i++) db.Quiz.Add(new Quiz { CourseId = course.Id, Title = $"Ẩn {i + 1}", IsActive = false });
        db.SaveChanges();
        return course;
    }

    private static async Task<PublicStatDto> Stat(ClassRoomWorld world)
    {
        var result = await world.Service<IReportService>().GetPublicStat();
        Assert.True(result.IsSuccess);
        return Assert.IsType<PublicStatDto>(result.Value);
    }

    [Fact]
    public async Task A_draft_course_adds_nothing_to_the_lesson_and_quiz_counts()
    {
        var world = ClassRoomWorld.Create();
        AddCourse(world, "NHAP", CourseStatus.Draft, lessons: 4, quizzes: 6);

        var stat = await Stat(world);

        Assert.Equal(0, stat.PublishedCourse);
        Assert.Equal(0, stat.TotalLesson);
        Assert.Equal(0, stat.TotalQuiz);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Archived)]
    [InlineData(CourseStatus.Rejected)]
    [InlineData(CourseStatus.PendingHeadApproval)]
    [InlineData(CourseStatus.PendingDirectorApproval)]
    [InlineData(CourseStatus.PendingHrApproval)]
    public async Task Only_published_courses_are_counted(CourseStatus status)
    {
        var world = ClassRoomWorld.Create();
        AddCourse(world, "DANG-HANH", CourseStatus.Published, lessons: 2, quizzes: 3);
        AddCourse(world, "KHAC", status, lessons: 5, quizzes: 7);

        var stat = await Stat(world);

        Assert.Equal(1, stat.PublishedCourse);
        Assert.Equal(2, stat.TotalLesson);
        Assert.Equal(3, stat.TotalQuiz);
    }

    [Fact]
    public async Task A_hidden_quiz_in_a_published_course_is_still_not_counted()
    {
        var world = ClassRoomWorld.Create();
        AddCourse(world, "DANG-HANH", CourseStatus.Published, lessons: 1, quizzes: 2, hiddenQuizzes: 3);

        var stat = await Stat(world);

        Assert.Equal(2, stat.TotalQuiz);
    }

    [Fact]
    public async Task Publishing_the_draft_makes_its_content_appear()
    {
        var world = ClassRoomWorld.Create();
        var course = AddCourse(world, "SAP-RA-MAT", CourseStatus.Draft, lessons: 2, quizzes: 6);
        Assert.Equal(0, (await Stat(world)).TotalQuiz);

        using (var db = world.NewDb())
        {
            db.Course.Single(c => c.Id == course.Id).Status = CourseStatus.Published;
            db.SaveChanges();
        }

        var stat = await Stat(world);
        Assert.Equal(6, stat.TotalQuiz);
        Assert.Equal(2, stat.TotalLesson);
    }
}
