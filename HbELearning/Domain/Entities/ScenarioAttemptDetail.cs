// File: Domain/Entities/ScenarioAttemptDetail.cs
namespace HbELearning.Domain.Entities;

/// <summary>Chi tiết lựa chọn xử lý từng điểm nguy cơ trong một lượt chơi.</summary>
public class ScenarioAttemptDetail
{
    public int Id { get; set; }

    public int ScenarioAttemptId { get; set; }
    public ScenarioAttempt ScenarioAttempt { get; set; } = null!;

    public int ScenarioNodeId { get; set; }
    public ScenarioNode ScenarioNode { get; set; } = null!;

    public int ScenarioChoiceId { get; set; }
    public ScenarioChoice ScenarioChoice { get; set; } = null!;

    public int PointsEarned { get; set; }
    public DateTime AnsweredAt { get; set; } = DateTime.Now;
}
