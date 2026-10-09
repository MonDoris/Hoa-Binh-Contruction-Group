// File: Domain/Entities/ScormRuntime.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Dữ liệu run-time (mô hình dữ liệu CMI) của MỘT SCO trong một <see cref="ScormRegistration"/>.
///
/// <see cref="CmiJson"/> giữ nguyên văn mọi phần tử nội dung đã ghi (vị trí đang học, suspend_data, interactions,
/// objectives...) của lượt thử gần nhất trên SCO này: lượt thử mới (cmi.entry = ab-initio) xóa sạch, học tiếp
/// (cmi.entry = resume) đọc lại đúng như cũ. Các cột tóm tắt chép ra cho báo cáo.</summary>
public class ScormRuntime
{
    public int Id { get; set; }

    public int ScormRegistrationId { get; set; }
    public ScormRegistration ScormRegistration { get; set; } = null!;

    /// <summary>identifier của &lt;item&gt; trong manifest.</summary>
    [Required, MaxLength(250)]
    public string ActivityId { get; set; } = null!;

    /// <summary>Dữ liệu CMI (JSON).</summary>
    public string? CmiJson { get; set; }

    [MaxLength(20)]
    public string CompletionStatus { get; set; } = "unknown";

    [MaxLength(20)]
    public string SuccessStatus { get; set; } = "unknown";

    public decimal? ScoreScaled { get; set; }
    public decimal? ScoreRaw { get; set; }
    public decimal? ScoreMin { get; set; }
    public decimal? ScoreMax { get; set; }
    public decimal? ProgressMeasure { get; set; }

    /// <summary>cmi.total_time của lượt thử hiện tại (giây).</summary>
    public int TotalTimeSecond { get; set; }

    /// <summary>Số lần SCO được mở (Initialize) trong lượt thử hiện tại.</summary>
    public int SessionCount { get; set; }

    public int InteractionCount { get; set; }

    public DateTime? FirstAccessedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
