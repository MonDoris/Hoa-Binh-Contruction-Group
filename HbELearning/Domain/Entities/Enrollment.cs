// File: Domain/Entities/Enrollment.cs

namespace HbELearning.Domain.Entities;

/// <summary>Bản ghi phân công khóa học cho một người học — trung tâm của luồng nghiệp vụ.</summary>
public class Enrollment
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Người phụ trách đào tạo đã phân công khóa học này.</summary>
    public int? AssignedById { get; set; }
    public User? AssignedBy { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Assigned;

    /// <summary>Tiến độ hoàn thành bài học (0–100).</summary>
    public decimal ProgressPercent { get; set; }

    /// <summary>Điểm bài kiểm tra cuối khóa cao nhất đã đạt.</summary>
    public decimal? FinalScore { get; set; }

    public ICollection<LessonProgress> LessonProgressList { get; set; } = new List<LessonProgress>();
    public ICollection<QuizAttempt> QuizAttemptList { get; set; } = new List<QuizAttempt>();
    public ICollection<ScenarioAttempt> ScenarioAttemptList { get; set; } = new List<ScenarioAttempt>();
    public Certificate? Certificate { get; set; }

    // ==================== HÀNH VI ====================

    /// <summary>Khóa học đã hoàn thành thì không hủy phân công được nữa — chứng nhận đã cấp dựa trên nó.</summary>
    public bool CanBeCancelled => Status != EnrollmentStatus.Completed;

    public void Cancel()
    {
        if (!CanBeCancelled)
            throw new InvalidOperationException("Không thể hủy khóa học đã hoàn thành.");
        Status = EnrollmentStatus.Cancelled;
    }

    /// <summary>
    /// Cập nhật tiến độ, điểm cuối khóa và trạng thái từ kết quả vừa tính trên lộ trình học.
    ///
    /// Quy tắc hoàn thành: xong TẤT CẢ bước bắt buộc (và phải có ít nhất một bước bắt buộc), đồng thời nếu khóa học
    /// có bài thi cuối khóa thì điểm cao nhất phải đạt điểm đỗ. Đã hoàn thành thì giữ nguyên mốc hoàn thành cũ —
    /// tính lại nhiều lần không được dời ngày hoàn thành trên chứng nhận.
    /// </summary>
    /// <param name="requiredStepCount">Số bước bắt buộc của lộ trình.</param>
    /// <param name="completedRequiredStepCount">Số bước bắt buộc đã hoàn thành.</param>
    /// <param name="hasFinalExam">Khóa học có bài kiểm tra đánh dấu thi cuối khóa (đang hoạt động) không.</param>
    /// <param name="bestFinalScore">Điểm cao nhất trong các lần nộp bài thi cuối khóa; null nếu chưa nộp lần nào.</param>
    /// <param name="passScore">Điểm đỗ của khóa học.</param>
    public void ApplyProgress(int requiredStepCount, int completedRequiredStepCount, bool hasFinalExam,
        decimal? bestFinalScore, decimal passScore, DateTime now)
    {
        ProgressPercent = requiredStepCount == 0
            ? 0m
            : Math.Round(completedRequiredStepCount * 100m / requiredStepCount, 2);

        FinalScore = bestFinalScore;

        if (StartedAt is null && ProgressPercent > 0)
            StartedAt = now;

        var allRequiredDone = requiredStepCount > 0 && completedRequiredStepCount == requiredStepCount;
        var scoreQualified = !hasFinalExam || (bestFinalScore ?? 0m) >= passScore;

        if (allRequiredDone && scoreQualified)
        {
            if (Status != EnrollmentStatus.Completed)
            {
                Status = EnrollmentStatus.Completed;
                CompletedAt = now;
            }
        }
        else if (ProgressPercent > 0 && Status == EnrollmentStatus.Assigned)
        {
            Status = EnrollmentStatus.InProgress;
        }
    }
}
