// File: Domain/Entities/AttemptDetail.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Chi tiết trả lời từng câu hỏi trong một lần làm bài.</summary>
public class AttemptDetail
{
    public int Id { get; set; }

    public int QuizAttemptId { get; set; }
    public QuizAttempt QuizAttempt { get; set; } = null!;

    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    /// <summary>Danh sách Id đáp án đã chọn, phân tách bằng dấu phẩy. VD: "12" hoặc "12,15".</summary>
    [MaxLength(200)]
    public string? SelectedAnswerId { get; set; }

    public bool IsCorrect { get; set; }
    public decimal EarnedScore { get; set; }
}
