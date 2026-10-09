// File: Application/Features/Scorm/Runtime/ScormRuntimeReport.cs

namespace HbELearning.Application.Features.Scorm.Runtime;

/// <summary>
/// Bản tóm tắt dữ liệu run-time của MỘT SCO mà trình phát gửi lên ở mỗi lần Commit / Terminate — đã quy về một
/// dạng chung cho cả SCORM 1.2 lẫn 2004, để engine sequencing chỉ phải hiểu một kiểu.
///
/// Trình phát (wwwroot/js/scorm-rte.js) là nơi áp các luật của RTE trước khi gửi: trạng thái hoàn thành đã tính
/// theo completion_threshold, trạng thái đạt đã tính theo scaled_passing_score (2004) hoặc mastery score (1.2).
/// </summary>
public sealed class ScormRuntimeReport
{
    /// <summary>completed | incomplete | not attempted | unknown.</summary>
    public string CompletionStatus { get; set; } = "unknown";

    /// <summary>passed | failed | unknown.</summary>
    public string SuccessStatus { get; set; } = "unknown";

    public double? ScoreScaled { get; set; }
    public double? ScoreRaw { get; set; }
    public double? ScoreMin { get; set; }
    public double? ScoreMax { get; set; }
    public double? ProgressMeasure { get; set; }

    /// <summary>cmi.exit (2004) / cmi.core.exit (1.2): suspend | normal | logout | time-out | "".</summary>
    public string? Exit { get; set; }

    /// <summary>Thời lượng phiên vừa rồi (giây) — cmi.session_time, hoặc thời gian đo được nếu SCO không báo.</summary>
    public double SessionTimeSecond { get; set; }

    public List<ScormObjectiveReport> ObjectiveList { get; set; } = new();

    /// <summary>adl.data.n.store mà SCO đã ghi trong phiên này: targetID -> nội dung.</summary>
    public Dictionary<string, string>? SharedDataWriteById { get; set; }
}

public sealed class ScormObjectiveReport
{
    public string Id { get; set; } = "";
    public string SuccessStatus { get; set; } = "unknown";
    public string CompletionStatus { get; set; } = "unknown";
    public double? ScoreScaled { get; set; }
    public double? ScoreRaw { get; set; }
    public double? ScoreMin { get; set; }
    public double? ScoreMax { get; set; }
    public double? ProgressMeasure { get; set; }
}
