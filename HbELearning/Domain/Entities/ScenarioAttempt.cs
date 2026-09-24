// File: Domain/Entities/ScenarioAttempt.cs

namespace HbELearning.Domain.Entities;

/// <summary>Một lượt chơi mô phỏng 3D của người học.</summary>
public class ScenarioAttempt
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public int ScenarioSceneId { get; set; }
    public ScenarioScene ScenarioScene { get; set; } = null!;

    /// <summary>Lần chơi thứ mấy (1, 2, 3...) — không giới hạn số lượt, khác Quiz.</summary>
    public int AttemptNo { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Điểm hiện tại — bắt đầu từ 0, có thể xuống âm, không giới hạn dưới.</summary>
    public int Score { get; set; }

    /// <summary>Tổng điểm tối đa có thể đạt (snapshot lúc bắt đầu = tổng PointDelta lớn nhất mỗi node).</summary>
    public int MaxScore { get; set; }

    public ScenarioAttemptStatus Status { get; set; } = ScenarioAttemptStatus.InProgress;
    public bool IsPassed { get; set; }

    public ICollection<ScenarioAttemptDetail> DetailList { get; set; } = new List<ScenarioAttemptDetail>();
}
