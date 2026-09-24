// File: Domain/Entities/Question.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Question
{
    public int Id { get; set; }

    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;

    [Required, MaxLength(2000)]
    public string Content { get; set; } = null!;

    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;

    /// <summary>Điểm của câu hỏi.</summary>
    public decimal Score { get; set; } = 1m;

    public int OrderIndex { get; set; }

    /// <summary>Giải thích đáp án, hiển thị sau khi nộp bài.</summary>
    [MaxLength(1000)]
    public string? Explanation { get; set; }

    public ICollection<Answer> AnswerList { get; set; } = new List<Answer>();
    public ICollection<AttemptDetail> AttemptDetailList { get; set; } = new List<AttemptDetail>();
}
