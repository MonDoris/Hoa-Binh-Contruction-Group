// File: Domain/Entities/QuizAttempt.cs
namespace HbELearning.Domain.Entities;

/// <summary>Một lần làm bài kiểm tra của người học.</summary>
public class QuizAttempt
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;

    /// <summary>Lần làm thứ mấy (1, 2, 3...).</summary>
    public int AttemptNo { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Điểm quy đổi theo thang 100.</summary>
    public decimal Score { get; set; }

    /// <summary>Tổng điểm tối đa của bài (tổng Question.Score).</summary>
    public decimal MaxScore { get; set; }

    public bool IsPassed { get; set; }

    public ICollection<AttemptDetail> AttemptDetailList { get; set; } = new List<AttemptDetail>();
}
