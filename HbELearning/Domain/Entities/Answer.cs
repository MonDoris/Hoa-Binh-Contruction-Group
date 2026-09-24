// File: Domain/Entities/Answer.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Answer
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    [Required, MaxLength(1000)]
    public string Content { get; set; } = null!;

    public bool IsCorrect { get; set; }
    public int OrderIndex { get; set; }
}
