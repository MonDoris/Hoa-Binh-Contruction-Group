// File: Application/Features/Scorm/Sequencing/ScormSequencingState.cs
// Mô hình theo dõi (Tracking Model) của SN cho MỘT người học trên MỘT gói — lưu JSON ở
// ScormRegistration.StateJson. Tên trường bám đúng thuật ngữ của SCORM 2004 4th Edition SN mục 4.2.

namespace HbELearning.Application.Features.Scorm.Sequencing;

public sealed class ScormSequencingState
{
    /// <summary>Current Activity — null ngoài phiên sequencing.</summary>
    public string? CurrentActivityId { get; set; }

    /// <summary>Suspended Activity — có giá trị nghĩa là lần sau "học tiếp" (Resume All) được.</summary>
    public string? SuspendedActivityId { get; set; }

    /// <summary>Hoạt động lá được giao gần nhất. Không thuộc mô hình của chuẩn — LMS dùng nó để đưa học viên về đúng
    /// chỗ khi Resume All không giao được gì (chuẩn ghi điểm tạm dừng là CỤM CHA nếu nội dung đã tự kết thúc trước
    /// lúc tạm dừng, mà một cụm thì không phát được).</summary>
    public string? LastDeliveredActivityId { get; set; }

    public Dictionary<string, ActivityState> ActivityById { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Mục tiêu toàn cục (Shared Global Objectives). Phạm vi: một registration — gói khai
    /// objectivesGlobalToSystem="true" vẫn chỉ chia sẻ mục tiêu giữa các SCO trong cùng gói, không xuyên gói.</summary>
    public Dictionary<string, ObjectiveState> GlobalObjectiveById { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Kho dữ liệu chia sẻ adl.data (4th Ed): targetID -> nội dung.</summary>
    public Dictionary<string, string> SharedDataById { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ActivityState
{
    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }

    /// <summary>Activity Progress Status — đã từng có ít nhất một lượt thử.</summary>
    public bool ProgressStatus { get; set; }
    public int AttemptCount { get; set; }

    /// <summary>Attempt Progress Status / Attempt Completion Status của lượt thử hiện tại.</summary>
    public bool AttemptProgressStatus { get; set; }
    public bool AttemptCompletionStatus { get; set; }

    /// <summary>Attempt Completion Amount (progress measure) và cờ "đã biết".</summary>
    public bool AttemptCompletionAmountStatus { get; set; }
    public double AttemptCompletionAmount { get; set; }

    public double AttemptExperiencedDurationSecond { get; set; }
    public double ActivityExperiencedDurationSecond { get; set; }

    /// <summary>Available Children sau chọn lọc / xáo trộn (Selection &amp; Randomization). Null = đủ con, đúng
    /// thứ tự trong manifest.</summary>
    public List<string>? AvailableChildIdList { get; set; }

    /// <summary>Mục tiêu cục bộ theo khóa ObjectiveDefinition.Key.</summary>
    public Dictionary<string, ObjectiveState> ObjectiveByKey { get; set; } = new(StringComparer.Ordinal);

    // ----- Phần ghi nhận của LMS (không thuộc mô hình theo dõi của chuẩn) -----
    // Lượt thử mới xóa trạng thái của lượt cũ — đúng chuẩn, nhưng lộ trình và báo cáo không được tụt "chưa xong" chỉ
    // vì học viên mở lại ôn tập. Ba trường dưới đây giữ kết quả TỐT NHẤT từng đạt.
    public bool EverCompleted { get; set; }
    public bool EverSatisfied { get; set; }
    public bool EverFailed { get; set; }
    public double? BestMeasure { get; set; }
}

public sealed class ObjectiveState
{
    public bool ProgressStatus { get; set; }
    public bool SatisfiedStatus { get; set; }
    public bool MeasureStatus { get; set; }
    public double NormalizedMeasure { get; set; }

    // Phần mở rộng 4th Ed (adlseq:mapInfo) — đi kèm cmi.objectives.n.score.raw/min/max, completion_status,
    // progress_measure để các SCO đọc lại được của nhau qua mục tiêu toàn cục.
    public double? RawScore { get; set; }
    public double? MinScore { get; set; }
    public double? MaxScore { get; set; }
    public string? CompletionStatus { get; set; }
    public double? ProgressMeasure { get; set; }
}
