// File: Application/DTOs/QuizDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

// ---------- Quản trị (Trainer/Admin) ----------
public record QuizListDto(int Id, int CourseId, string Title, decimal PassScore,
    int TimeLimitMinutes, int MaxAttempt, bool IsFinalExam, int QuestionCount, bool IsActive,
    int? LessonId, string? LessonTitle);

public record QuizManageDto(int Id, int CourseId, string CourseTitle, string Title, string? Description,
    decimal PassScore, int TimeLimitMinutes, int MaxAttempt, bool IsFinalExam, bool ShuffleQuestion,
    bool IsActive, int? LessonId, string? LessonTitle, IReadOnlyList<QuestionManageDto> QuestionList);

public record QuestionManageDto(int Id, string Content, string QuestionType, decimal Score,
    int OrderIndex, string? Explanation, IReadOnlyList<AnswerManageDto> AnswerList);

public record AnswerManageDto(int Id, string Content, bool IsCorrect, int OrderIndex);

public class QuizRequest
{
    [Required] public int CourseId { get; set; }

    /// <summary>Chương của cấu trúc khóa học sẽ chứa nội dung mới (chỉ dùng lúc tạo); null = tự xếp chỗ.</summary>
    public int? ParentItemId { get; set; }

    /// <summary>Bài học mà bài kiểm tra này gắn theo (nếu có) — null = bài kiểm tra độc lập (VD thi cuối khóa).</summary>
    public int? LessonId { get; set; }

    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [MaxLength(1000)] public string? Description { get; set; }
    [Range(0, 100)] public decimal PassScore { get; set; } = 70m;
    [Range(1, 600)] public int TimeLimitMinutes { get; set; } = 30;
    [Range(1, 20)] public int MaxAttempt { get; set; } = 3;
    public bool IsFinalExam { get; set; }
    public bool ShuffleQuestion { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class QuestionRequest
{
    [Required, MaxLength(2000)] public string Content { get; set; } = null!;
    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;
    [Range(0.1, 1000)] public decimal Score { get; set; } = 1m;
    public int OrderIndex { get; set; }
    [MaxLength(1000)] public string? Explanation { get; set; }

    [Required, MinLength(2, ErrorMessage = "Câu hỏi phải có ít nhất 2 đáp án")]
    public List<AnswerRequest> AnswerList { get; set; } = new();
}

public class AnswerRequest
{
    [Required, MaxLength(1000)] public string Content { get; set; } = null!;
    public bool IsCorrect { get; set; }
    public int OrderIndex { get; set; }
}

// ---------- Làm bài (Learner) — không lộ đáp án đúng ----------
public record QuizStartDto(int AttemptId, int QuizId, string QuizTitle, int AttemptNo,
    int MaxAttempt, int TimeLimitMinutes, DateTime StartedAt, DateTime DeadlineAt,
    decimal PassScore, IReadOnlyList<QuestionTakeDto> QuestionList);

public record QuestionTakeDto(int Id, string Content, string QuestionType, decimal Score,
    IReadOnlyList<AnswerTakeDto> AnswerList);

public record AnswerTakeDto(int Id, string Content);

public class SubmitQuizRequest
{
    public List<SubmitAnswerItem> AnswerList { get; set; } = new();
}

public class SubmitAnswerItem
{
    [Required] public int QuestionId { get; set; }
    public List<int> SelectedAnswerIdList { get; set; } = new();
}

public record QuizResultDto(int AttemptId, int QuizId, string QuizTitle, int AttemptNo,
    decimal Score, decimal MaxScore, decimal PassScore, bool IsPassed,
    DateTime StartedAt, DateTime? SubmittedAt, int CorrectCount, int TotalQuestion,
    IReadOnlyList<QuizResultItemDto> ItemList);

public record QuizResultItemDto(int QuestionId, string Content, bool IsCorrect,
    decimal EarnedScore, decimal MaxScore, string? Explanation,
    IReadOnlyList<int> SelectedAnswerIdList, IReadOnlyList<int> CorrectAnswerIdList);
