using HbELearning.Application.Common.Results;
using HbELearning.Application.DTOs;
using HbELearning.Application.Features.Quizzes;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Tests.Quizzes;

/// <summary>
/// Sửa câu hỏi trong Ngân hàng câu hỏi. Điều cần giữ: bài đã làm lưu "đáp án đã chọn" bằng danh sách Id đáp án
/// (AttemptDetail.SelectedAnswerId — không có khóa ngoại), nên sửa câu hỏi mà đổi Id đáp án là màn xem lại bài làm
/// cũ trỏ vào đáp án không còn tồn tại. Vì vậy đáp án phải được cập nhật TẠI CHỖ theo Id.
/// </summary>
public class UpdateQuestionTests
{
    private sealed record Seed(ClassRoomWorld World, int QuizId, int QuestionId, List<int> AnswerIdList);

    /// <summary>Một khóa học có một bài kiểm tra, câu hỏi thứ 5 gồm 3 đáp án A/B/C (B đúng).</summary>
    private static Seed OneQuestion()
    {
        var world = ClassRoomWorld.Create();
        using var db = world.NewDb();

        var course = new Course
        {
            Code = "KH-TEST", Title = "Khóa thử", CreatedById = world.Host.Id,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
        };
        db.Course.Add(course);
        db.SaveChanges();

        var quiz = new Quiz { CourseId = course.Id, Title = "Kiểm tra thử" };
        db.Quiz.Add(quiz);
        db.SaveChanges();

        var question = new Question
        {
            QuizId = quiz.Id, Content = "Câu gốc", QuestionType = QuestionType.SingleChoice, Score = 1m, OrderIndex = 5,
            AnswerList =
            {
                new Answer { Content = "A", IsCorrect = false, OrderIndex = 1 },
                new Answer { Content = "B", IsCorrect = true, OrderIndex = 2 },
                new Answer { Content = "C", IsCorrect = false, OrderIndex = 3 },
            },
        };
        db.Question.Add(question);
        db.SaveChanges();

        return new Seed(world, quiz.Id, question.Id, question.AnswerList.OrderBy(a => a.OrderIndex).Select(a => a.Id).ToList());
    }

    private static QuestionRequest Request(params AnswerRequest[] answerList) => new()
    {
        Content = "Câu đã sửa", QuestionType = QuestionType.SingleChoice, Score = 2m, OrderIndex = 0,
        Explanation = "Giải thích mới", AnswerList = answerList.ToList(),
    };

    private static AnswerRequest Answer(int? id, string content, bool correct) => new() { Id = id, Content = content, IsCorrect = correct };

    private static List<Answer> AnswersOf(ClassRoomWorld world, int questionId)
    {
        using var db = world.NewDb();
        return db.Answer.Where(a => a.QuestionId == questionId).OrderBy(a => a.OrderIndex).ToList();
    }

    [Fact]
    public async Task Editing_keeps_the_ids_of_answers_that_stay()
    {
        var s = OneQuestion();
        var (a, b, c) = (s.AnswerIdList[0], s.AnswerIdList[1], s.AnswerIdList[2]);

        // Sửa chữ A, giữ B, bỏ C, thêm D mới.
        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(a, "A đã sửa", false), Answer(b, "B", true), Answer(null, "D", false)));

        Assert.True(result.IsSuccess);
        var after = AnswersOf(s.World, s.QuestionId);
        Assert.Equal(new[] { "A đã sửa", "B", "D" }, after.Select(x => x.Content));

        // A và B giữ nguyên Id → bài đã làm chọn A hoặc B vẫn thấy đáp án của mình.
        Assert.Equal(a, after[0].Id);
        Assert.Equal(b, after[1].Id);
        Assert.DoesNotContain(after, x => x.Id == c);   // C bị bỏ khỏi yêu cầu nên bị xóa.
        Assert.NotEqual(0, after[2].Id);
        Assert.True(after[1].IsCorrect);
    }

    [Fact]
    public async Task Editing_can_change_which_answer_is_correct_without_changing_ids()
    {
        var s = OneQuestion();
        var (a, b) = (s.AnswerIdList[0], s.AnswerIdList[1]);

        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(a, "A", true), Answer(b, "B", false), Answer(s.AnswerIdList[2], "C", false)));

        Assert.True(result.IsSuccess);
        var after = AnswersOf(s.World, s.QuestionId);
        Assert.Equal(s.AnswerIdList, after.Select(x => x.Id));
        Assert.Equal(new[] { true, false, false }, after.Select(x => x.IsCorrect));
    }

    [Fact]
    public async Task Editing_updates_the_question_fields_and_keeps_its_position_when_no_order_is_sent()
    {
        var s = OneQuestion();

        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(s.AnswerIdList[0], "A", false), Answer(s.AnswerIdList[1], "B", true)));

        Assert.True(result.IsSuccess);
        using var db = s.World.NewDb();
        var question = await db.Question.SingleAsync(q => q.Id == s.QuestionId);
        Assert.Equal("Câu đã sửa", question.Content);
        Assert.Equal(2m, question.Score);
        Assert.Equal("Giải thích mới", question.Explanation);
        Assert.Equal(5, question.OrderIndex);   // Màn sửa không gửi thứ tự: không được tụt về 0 (đầu bài).
    }

    [Fact]
    public async Task An_answer_id_from_another_question_is_treated_as_new_and_leaves_that_answer_alone()
    {
        var s = OneQuestion();
        int otherAnswerId;
        using (var db = s.World.NewDb())
        {
            var other = new Question
            {
                QuizId = s.QuizId, Content = "Câu khác", OrderIndex = 6,
                AnswerList = { new Answer { Content = "X", IsCorrect = true, OrderIndex = 1 }, new Answer { Content = "Y", OrderIndex = 2 } },
            };
            db.Question.Add(other);
            db.SaveChanges();
            otherAnswerId = other.AnswerList.First().Id;
        }

        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(s.AnswerIdList[1], "B", true), Answer(otherAnswerId, "Mượn Id", false)));

        Assert.True(result.IsSuccess);
        using var check = s.World.NewDb();
        var stolen = await check.Answer.SingleAsync(x => x.Id == otherAnswerId);
        Assert.Equal("X", stolen.Content);   // Đáp án của câu khác không bị đụng tới.
        Assert.Equal(1, await check.Answer.CountAsync(x => x.QuestionId == s.QuestionId && x.Content == "Mượn Id"));
    }

    [Fact]
    public async Task A_client_that_sends_no_ids_still_replaces_the_answers()
    {
        var s = OneQuestion();

        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(null, "Mới 1", false), Answer(null, "Mới 2", true)));

        Assert.True(result.IsSuccess);
        var after = AnswersOf(s.World, s.QuestionId);
        Assert.Equal(new[] { "Mới 1", "Mới 2" }, after.Select(x => x.Content));
    }

    [Fact]
    public async Task Invalid_edit_changes_nothing()
    {
        var s = OneQuestion();

        // Không có đáp án đúng nào.
        var result = await s.World.Service<IQuizService>().UpdateQuestion(s.QuestionId,
            Request(Answer(s.AnswerIdList[0], "A sửa dở", false), Answer(s.AnswerIdList[1], "B", false)));

        Assert.Equal(ResultKind.Invalid, result.Kind);
        var after = AnswersOf(s.World, s.QuestionId);
        Assert.Equal(new[] { "A", "B", "C" }, after.Select(x => x.Content));
        Assert.Equal(new[] { false, true, false }, after.Select(x => x.IsCorrect));
    }
}
