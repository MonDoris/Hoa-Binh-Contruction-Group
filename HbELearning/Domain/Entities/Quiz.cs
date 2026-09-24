// File: Domain/Entities/Quiz.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Quiz
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Bài học mà bài kiểm tra này gắn theo (nếu có) — null = bài kiểm tra độc lập (VD thi cuối khóa).</summary>
    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Điểm tối thiểu (%) để đạt bài kiểm tra.</summary>
    public decimal PassScore { get; set; } = 70m;

    public int TimeLimitMinutes { get; set; } = 30;

    /// <summary>Số lần làm bài tối đa.</summary>
    public int MaxAttempt { get; set; } = 3;

    /// <summary>True = bài kiểm tra cuối khóa, dùng để xét cấp chứng nhận.</summary>
    public bool IsFinalExam { get; set; }

    public bool ShuffleQuestion { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Question> QuestionList { get; set; } = new List<Question>();
    public ICollection<QuizAttempt> QuizAttemptList { get; set; } = new List<QuizAttempt>();
    public ICollection<PathItem> PathItemList { get; set; } = new List<PathItem>();
}
