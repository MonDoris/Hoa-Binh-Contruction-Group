// File: Domain/Entities/LessonProgress.cs

namespace HbELearning.Domain.Entities;

/// <summary>Tiến độ học của một người học trên một bài học cụ thể.</summary>
public class LessonProgress
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public ProgressStatus Status { get; set; } = ProgressStatus.NotStarted;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Tổng thời gian đã học bài này (giây).</summary>
    public int TimeSpentSecond { get; set; }

    /// <summary>Vị trí đang xem dở của video (giây) — để học tiếp lần sau.</summary>
    public int LastPositionSecond { get; set; }

    /// <summary>Mốc xa nhất học viên đã thực sự xem tới (giây) — chỉ tăng, không bao giờ giảm.
    /// Khác <see cref="LastPositionSecond"/> ở chỗ tua lùi xem lại không kéo giá trị này xuống, nhờ vậy bài học
    /// đặt <see cref="VideoSeekPolicy.WatchFull"/> vẫn biết học viên đã xem tới đâu sau khi tải lại trang —
    /// nếu dùng LastPositionSecond thì chỉ cần tua lùi rồi F5 là mất luôn phần đã xem.</summary>
    public int MaxWatchedSecond { get; set; }

    // ==================== HÀNH VI ====================

    /// <summary>Đánh dấu học viên đã hoàn thành bài học.</summary>
    public void MarkCompleted(DateTime now)
    {
        Status = ProgressStatus.Completed;
        CompletedAt = now;
    }
}
