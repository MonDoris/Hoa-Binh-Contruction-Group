// File: Domain/Entities/ScenarioChoice.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một lựa chọn xử lý cho một điểm nguy cơ — điểm có thể âm.</summary>
public class ScenarioChoice
{
    public int Id { get; set; }

    public int ScenarioNodeId { get; set; }
    public ScenarioNode ScenarioNode { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Content { get; set; } = null!;

    public bool IsCorrect { get; set; }

    /// <summary>Điểm cộng/trừ khi học viên chọn lựa chọn này — có thể âm.</summary>
    public int PointDelta { get; set; }

    /// <summary>Giải thích hiển thị ngay sau khi học viên chọn.</summary>
    [MaxLength(1000)]
    public string? Feedback { get; set; }

    public int OrderIndex { get; set; }
}
