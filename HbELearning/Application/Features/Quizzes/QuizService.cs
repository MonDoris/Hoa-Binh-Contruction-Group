// File: Application/Features/Quizzes/QuizService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Quizzes;

public class QuizService : IQuizService
{
    private readonly IAppDbContext _db;
    private readonly ILearningService _learningService;
    private readonly ICurrentUser _currentUser;
    private readonly ILearningPathService _learningPathService;

    public QuizService(IAppDbContext db, ILearningService learningService, ICurrentUser currentUser,
        ILearningPathService learningPathService)
    {
        _learningPathService = learningPathService;
        _db = db;
        _learningService = learningService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetByCourse(int courseId)
    {
        var list = await _db.Quiz.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.IsActive)
            .OrderBy(x => x.IsFinalExam).ThenBy(x => x.Id)
            .Select(x => new QuizListDto(x.Id, x.CourseId, x.Title, x.PassScore, x.TimeLimitMinutes,
                x.MaxAttempt, x.IsFinalExam, x.QuestionList.Count, x.IsActive,
                x.LessonId, x.Lesson != null ? x.Lesson.Title : null))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetForManage(int id)
    {
        var quiz = await _db.Quiz.AsNoTracking()
            .Include(x => x.Course)
            .Include(x => x.Lesson)
            .Include(x => x.QuestionList).ThenInclude(q => q.AnswerList)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (quiz is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));

        var questionList = quiz.QuestionList.OrderBy(q => q.OrderIndex).Select(q => new QuestionManageDto(
            q.Id, q.Content, q.QuestionType.ToString(), q.Score, q.OrderIndex, q.Explanation,
            q.AnswerList.OrderBy(a => a.OrderIndex)
                .Select(a => new AnswerManageDto(a.Id, a.Content, a.IsCorrect, a.OrderIndex)).ToList()))
            .ToList();

        return Result.Success(new QuizManageDto(quiz.Id, quiz.CourseId, quiz.Course.Title, quiz.Title, quiz.Description,
            quiz.PassScore, quiz.TimeLimitMinutes, quiz.MaxAttempt, quiz.IsFinalExam,
            quiz.ShuffleQuestion, quiz.IsActive, quiz.LessonId, quiz.Lesson?.Title, questionList));
    }

    public async Task<Result> Create(QuizRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == request.CourseId))
            return Result.Invalid(new MessageResponse("Khóa học không tồn tại."));

        var quiz = new Quiz
        {
            CourseId = request.CourseId,
            LessonId = request.LessonId,
            Title = request.Title.Trim(),
            Description = request.Description,
            PassScore = request.PassScore,
            TimeLimitMinutes = request.TimeLimitMinutes,
            MaxAttempt = request.MaxAttempt,
            IsFinalExam = request.IsFinalExam,
            ShuffleQuestion = request.ShuffleQuestion,
            IsActive = request.IsActive
        };

        _db.Quiz.Add(quiz);
        await _db.SaveChangesAsync();
        await _learningPathService.AppendResourceAsync(quiz.CourseId, PathItemType.Quiz, quiz.Id, request.ParentItemId);
        return Result.Success(new MessageResponse($"Đã tạo bài kiểm tra #{quiz.Id}.", quiz.Id));
    }

    public async Task<Result> Update(int id, QuizRequest request)
    {
        var quiz = await _db.Quiz.FirstOrDefaultAsync(x => x.Id == id);
        if (quiz is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));

        quiz.LessonId = request.LessonId;
        quiz.Title = request.Title.Trim();
        quiz.Description = request.Description;
        quiz.PassScore = request.PassScore;
        quiz.TimeLimitMinutes = request.TimeLimitMinutes;
        quiz.MaxAttempt = request.MaxAttempt;
        quiz.IsFinalExam = request.IsFinalExam;
        quiz.ShuffleQuestion = request.ShuffleQuestion;
        quiz.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    public async Task<Result> Delete(int id)
    {
        var quiz = await _db.Quiz.FirstOrDefaultAsync(x => x.Id == id);
        if (quiz is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));

        if (await _db.QuizAttempt.AnyAsync(x => x.QuizId == id))
        {
            quiz.IsActive = false;
            await _db.SaveChangesAsync();
            return Result.Success(new MessageResponse("Bài kiểm tra đã có người làm nên được ẩn thay vì xóa."));
        }

        var pathItemList = await _db.PathItem.Where(x => x.QuizId == id).ToListAsync();
        _db.PathItem.RemoveRange(pathItemList);
        _db.Quiz.Remove(quiz);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa bài kiểm tra."));
    }

    public async Task<Result> AddQuestion(int quizId, QuestionRequest request)
    {
        if (!await _db.Quiz.AnyAsync(x => x.Id == quizId))
            return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));

        var error = ValidateAnswer(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var maxOrder = await _db.Question.Where(x => x.QuizId == quizId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var question = new Question
        {
            QuizId = quizId,
            Content = request.Content.Trim(),
            QuestionType = request.QuestionType,
            Score = request.Score,
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            Explanation = request.Explanation
        };

        var order = 1;
        foreach (var answer in request.AnswerList)
        {
            question.AnswerList.Add(new Answer
            {
                Content = answer.Content.Trim(),
                IsCorrect = answer.IsCorrect,
                OrderIndex = answer.OrderIndex > 0 ? answer.OrderIndex : order
            });
            order++;
        }

        _db.Question.Add(question);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã thêm câu hỏi #{question.Id}."));
    }

    public async Task<Result> UpdateQuestion(int questionId, QuestionRequest request)
    {
        var question = await _db.Question.Include(x => x.AnswerList)
            .FirstOrDefaultAsync(x => x.Id == questionId);
        if (question is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu hỏi."));

        var error = ValidateAnswer(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        question.Content = request.Content.Trim();
        question.QuestionType = request.QuestionType;
        question.Score = request.Score;
        question.OrderIndex = request.OrderIndex;
        question.Explanation = request.Explanation;

        _db.Answer.RemoveRange(question.AnswerList);

        var order = 1;
        foreach (var answer in request.AnswerList)
        {
            question.AnswerList.Add(new Answer
            {
                Content = answer.Content.Trim(),
                IsCorrect = answer.IsCorrect,
                OrderIndex = answer.OrderIndex > 0 ? answer.OrderIndex : order
            });
            order++;
        }

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật câu hỏi thành công."));
    }

    public async Task<Result> DeleteQuestion(int questionId)
    {
        var question = await _db.Question.FirstOrDefaultAsync(x => x.Id == questionId);
        if (question is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu hỏi."));

        if (await _db.AttemptDetail.AnyAsync(x => x.QuestionId == questionId))
            return Result.Invalid(new MessageResponse("Câu hỏi đã được sử dụng trong bài làm, không thể xóa."));

        _db.Question.Remove(question);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa câu hỏi."));
    }

    private static string? ValidateAnswer(QuestionRequest request)
    {
        var correctCount = request.AnswerList.Count(x => x.IsCorrect);
        if (correctCount == 0) return "Phải có ít nhất một đáp án đúng.";

        if (request.QuestionType != QuestionType.MultipleChoice && correctCount > 1)
            return "Câu hỏi một lựa chọn chỉ được có duy nhất một đáp án đúng.";

        if (request.QuestionType == QuestionType.TrueFalse && request.AnswerList.Count != 2)
            return "Câu hỏi Đúng/Sai phải có đúng 2 đáp án.";

        return null;
    }

    public async Task<Result> Start(int quizId, int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var quiz = await _db.Quiz.AsNoTracking()
            .Include(x => x.QuestionList).ThenInclude(q => q.AnswerList)
            .FirstOrDefaultAsync(x => x.Id == quizId && x.IsActive);

        if (quiz is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));
        if (quiz.CourseId != enrollment.CourseId)
            return Result.Invalid(new MessageResponse("Bài kiểm tra không thuộc khóa học này."));
        if (quiz.QuestionList.Count == 0)
            return Result.Invalid(new MessageResponse("Bài kiểm tra chưa có câu hỏi."));

        // Kiểm tra mở khóa theo lộ trình
        var pathList = await _learningService.BuildPathProgressAsync(enrollmentId);
        var step = pathList.FirstOrDefault(x => x.QuizId == quizId);
        if (step is not null && !step.IsUnlocked)
            return Result.Invalid(new MessageResponse("Bạn cần hoàn thành các bước trước đó trong lộ trình."));

        // Giới hạn lượt làm bài tính theo ngày hiện tại — mỗi ngày mới lượt làm bài được làm mới (reset).
        var todayStart = DateTime.Now.Date;
        var totalAttempt = await _db.QuizAttempt
            .CountAsync(x => x.EnrollmentId == enrollmentId && x.QuizId == quizId);
        var todayAttempt = await _db.QuizAttempt
            .CountAsync(x => x.EnrollmentId == enrollmentId && x.QuizId == quizId && x.StartedAt >= todayStart);
        if (todayAttempt >= quiz.MaxAttempt)
            return Result.Invalid(new MessageResponse($"Bạn đã dùng hết {quiz.MaxAttempt} lượt làm bài hôm nay. Lượt làm bài sẽ được làm mới vào ngày mai."));

        var attempt = new QuizAttempt
        {
            EnrollmentId = enrollmentId,
            QuizId = quizId,
            AttemptNo = totalAttempt + 1,
            StartedAt = DateTime.Now,
            MaxScore = quiz.QuestionList.Sum(q => q.Score)
        };
        _db.QuizAttempt.Add(attempt);
        await _db.SaveChangesAsync();

        var questionSource = quiz.QuestionList.AsEnumerable();
        questionSource = quiz.ShuffleQuestion
            ? questionSource.OrderBy(_ => Guid.NewGuid())
            : questionSource.OrderBy(q => q.OrderIndex);

        var questionList = questionSource.Select(q => new QuestionTakeDto(
            q.Id, q.Content, q.QuestionType.ToString(), q.Score,
            q.AnswerList.OrderBy(a => a.OrderIndex)
                .Select(a => new AnswerTakeDto(a.Id, a.Content)).ToList()))
            .ToList();

        return Result.Success(new QuizStartDto(attempt.Id, quiz.Id, quiz.Title, todayAttempt + 1, quiz.MaxAttempt,
            quiz.TimeLimitMinutes, attempt.StartedAt, attempt.StartedAt.AddMinutes(quiz.TimeLimitMinutes),
            quiz.PassScore, questionList));
    }

    public async Task<Result> Submit(int attemptId, SubmitQuizRequest request)
    {
        var attempt = await _db.QuizAttempt
            .Include(x => x.Enrollment)
            .Include(x => x.Quiz).ThenInclude(q => q.QuestionList).ThenInclude(q => q.AnswerList)
            .FirstOrDefaultAsync(x => x.Id == attemptId);

        if (attempt is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt làm bài."));
        if (attempt.Enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        if (attempt.SubmittedAt is not null)
            return Result.Invalid(new MessageResponse("Lượt làm bài này đã được nộp."));

        decimal earnedTotal = 0m;
        var correctCount = 0;
        var detailList = new List<AttemptDetail>();

        foreach (var question in attempt.Quiz.QuestionList)
        {
            var submitted = request.AnswerList.FirstOrDefault(x => x.QuestionId == question.Id);
            var selectedIdSet = submitted?.SelectedAnswerIdList?.Distinct().OrderBy(x => x).ToList() ?? new List<int>();
            var correctIdSet = question.AnswerList.Where(a => a.IsCorrect)
                .Select(a => a.Id).OrderBy(x => x).ToList();

            var isCorrect = selectedIdSet.Count > 0 && selectedIdSet.SequenceEqual(correctIdSet);
            var earned = isCorrect ? question.Score : 0m;

            earnedTotal += earned;
            if (isCorrect) correctCount++;

            detailList.Add(new AttemptDetail
            {
                QuizAttemptId = attempt.Id,
                QuestionId = question.Id,
                SelectedAnswerId = selectedIdSet.Count > 0 ? string.Join(',', selectedIdSet) : null,
                IsCorrect = isCorrect,
                EarnedScore = earned
            });
        }

        attempt.MaxScore = attempt.Quiz.QuestionList.Sum(q => q.Score);
        attempt.Score = attempt.MaxScore > 0
            ? Math.Round(earnedTotal * 100m / attempt.MaxScore, 2)
            : 0m;
        attempt.IsPassed = attempt.Score >= attempt.Quiz.PassScore;
        attempt.SubmittedAt = DateTime.Now;

        _db.AttemptDetail.AddRange(detailList);
        await _db.SaveChangesAsync();

        // Cập nhật tiến độ khóa học + cấp chứng nhận nếu đủ điều kiện
        await _learningService.RecalculateAsync(attempt.EnrollmentId);

        return await GetResult(attemptId);
    }

    public async Task<Result> GetResult(int attemptId)
    {
        var attempt = await _db.QuizAttempt.AsNoTracking()
            .Include(x => x.Enrollment)
            .Include(x => x.Quiz).ThenInclude(q => q.QuestionList).ThenInclude(q => q.AnswerList)
            .Include(x => x.AttemptDetailList)
            .FirstOrDefaultAsync(x => x.Id == attemptId);

        if (attempt is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt làm bài."));
        if (!_currentUser.IsManager && attempt.Enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var itemList = attempt.Quiz.QuestionList.OrderBy(q => q.OrderIndex).Select(q =>
        {
            var detail = attempt.AttemptDetailList.FirstOrDefault(d => d.QuestionId == q.Id);
            var selectedIdList = string.IsNullOrEmpty(detail?.SelectedAnswerId)
                ? new List<int>()
                : detail!.SelectedAnswerId!.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToList();

            return new QuizResultItemDto(q.Id, q.Content, detail?.IsCorrect ?? false,
                detail?.EarnedScore ?? 0m, q.Score, q.Explanation,
                selectedIdList,
                q.AnswerList.Where(a => a.IsCorrect).Select(a => a.Id).ToList());
        }).ToList();

        return Result.Success(new QuizResultDto(attempt.Id, attempt.QuizId, attempt.Quiz.Title, attempt.AttemptNo,
            attempt.Score, attempt.MaxScore, attempt.Quiz.PassScore, attempt.IsPassed,
            attempt.StartedAt, attempt.SubmittedAt,
            itemList.Count(x => x.IsCorrect), itemList.Count, itemList));
    }

    public async Task<Result> GetMyAttempt(int quizId, int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (!_currentUser.IsManager && enrollment.UserId != _currentUser.UserId) return Result.Forbidden();

        var list = await _db.QuizAttempt.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId && x.QuizId == quizId)
            .OrderByDescending(x => x.AttemptNo)
            .Select(x => new { x.Id, x.AttemptNo, x.Score, x.MaxScore, x.IsPassed, x.StartedAt, x.SubmittedAt })
            .ToListAsync();

        return Result.Success(list);
    }
}
