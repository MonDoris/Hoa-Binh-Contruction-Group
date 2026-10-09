// File: Application/Features/Scorm/Model/ScormActivityDefinition.cs
// Cây hoạt động đọc từ imsmanifest.xml — phần ĐỊNH NGHĨA (bất biến theo gói), lưu dạng JSON ở
// ScormPackage.ActivityTreeJson. Phần TRẠNG THÁI của từng người học nằm riêng ở Sequencing/ScormSequencingState.
//
// Tên và giá trị mặc định bám đúng SCORM 2004 4th Edition — Content Aggregation Model (CAM) mục 5 "Sequencing
// Definition Model" và Sequencing & Navigation (SN). Mặc định ở đây là mặc định của CHUẨN, không phải lựa chọn
// riêng: thẻ nào manifest bỏ trống thì engine phải hành xử như chuẩn quy định.

namespace HbELearning.Application.Features.Scorm.Model;

/// <summary>Toàn bộ cây của một gói, kèm vài thuộc tính cấp tổ chức.</summary>
public sealed class ScormActivityTree
{
    public ScormVersion Version { get; set; }
    public string? SchemaVersion { get; set; }
    public string? ManifestIdentifier { get; set; }
    public string OrganizationId { get; set; } = "";

    /// <summary>adlseq:objectivesGlobalToSystem (4th Ed). Hệ thống hiện giữ mục tiêu toàn cục trong phạm vi một
    /// registration — xem ghi chú ở ScormSequencingState.</summary>
    public bool ObjectivesGlobalToSystem { get; set; } = true;

    /// <summary>adlcp:sharedDataGlobalToSystem (4th Ed).</summary>
    public bool SharedDataGlobalToSystem { get; set; } = true;

    /// <summary>Hoạt động gốc = chính thẻ &lt;organization&gt;.</summary>
    public ScormActivityDefinition Root { get; set; } = new();

    /// <summary>Duyệt tiền thứ tự (preorder) — đúng thứ tự "forward" của SN.</summary>
    public IEnumerable<ScormActivityDefinition> Preorder()
    {
        var stack = new Stack<ScormActivityDefinition>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var activity = stack.Pop();
            yield return activity;
            for (var i = activity.Children.Count - 1; i >= 0; i--) stack.Push(activity.Children[i]);
        }
    }
}

/// <summary>Một hoạt động (= một &lt;item&gt;, hoặc &lt;organization&gt; với hoạt động gốc).</summary>
public sealed class ScormActivityDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>isvisible — false thì không hiện trong mục lục (vẫn tồn tại và vẫn được sequencing xét).</summary>
    public bool IsVisible { get; set; } = true;

    public string? ResourceId { get; set; }

    /// <summary>Địa chỉ mở nội dung, TƯƠNG ĐỐI so với gốc gói (đã ghép xml:base và nối parameters của item).
    /// Có thể là địa chỉ tuyệt đối http(s) nếu resource trỏ ra ngoài. Null với hoạt động cụm (cluster).</summary>
    public string? LaunchUrl { get; set; }

    /// <summary>scormType="sco" — nội dung gọi API. false = asset, chỉ hiển thị.</summary>
    public bool IsSco { get; set; }

    /// <summary>Bộ API nội dung này tìm: "1.2" (window.API) hay null = 2004 (window.API_1484_11). Một khóa học có thể
    /// nhúng gói 1.2 bên cạnh các SCO 2004, nên phiên bản là thuộc tính của TỪNG hoạt động chứ không của cả cây.</summary>
    public string? RuntimeVersion { get; set; }

    /// <summary>adlcp:dataFromLMS → cmi.launch_data.</summary>
    public string? DataFromLms { get; set; }

    /// <summary>adlcp:timeLimitAction → cmi.time_limit_action.</summary>
    public string? TimeLimitAction { get; set; }

    /// <summary>cmi.max_time_allowed — 2004 lấy từ imsss:limitConditions attemptAbsoluteDurationLimit, 1.2 lấy từ
    /// adlcp:maxtimeallowed (dạng HHHH:MM:SS.SS).</summary>
    public string? MaxTimeAllowed { get; set; }

    /// <summary>adlcp:masteryscore (chỉ SCORM 1.2) — điểm thô tối thiểu để LMS ghi passed/failed.</summary>
    public decimal? MasteryScore { get; set; }

    /// <summary>adlcp:completionThreshold (4th Ed).</summary>
    public CompletionThresholdDefinition? CompletionThreshold { get; set; }

    /// <summary>adlnav:presentation/navigationInterface/hideLMSUI — nút điều hướng LMS phải ẩn khi phát hoạt động
    /// này: continue, previous, exit, exitAll, abandon, abandonAll, suspendAll.</summary>
    public List<string> HideLmsUi { get; set; } = new();

    /// <summary>adlcp:data/adlcp:map — các kho dữ liệu chia sẻ (adl.data) mà SCO này đọc / ghi.</summary>
    public List<SharedDataMapDefinition> SharedDataList { get; set; } = new();

    public SequencingDefinition Sequencing { get; set; } = new();

    public List<ScormActivityDefinition> Children { get; set; } = new();

    public bool IsLeaf => Children.Count == 0;
}

public sealed class CompletionThresholdDefinition
{
    public bool CompletedByMeasure { get; set; }
    public double MinProgressMeasure { get; set; } = 1.0;
    public double ProgressWeight { get; set; } = 1.0;
}

public sealed class SharedDataMapDefinition
{
    public string TargetId { get; set; } = "";
    public bool ReadSharedData { get; set; } = true;
    public bool WriteSharedData { get; set; } = true;
}

/// <summary>imsss:sequencing của một hoạt động (đã trộn với sequencingCollection nếu có IDRef).</summary>
public sealed class SequencingDefinition
{
    // ----- controlMode -----
    public bool ControlChoice { get; set; } = true;
    public bool ControlChoiceExit { get; set; } = true;
    public bool ControlFlow { get; set; }
    public bool ControlForwardOnly { get; set; }
    public bool UseCurrentAttemptObjectiveInfo { get; set; } = true;
    public bool UseCurrentAttemptProgressInfo { get; set; } = true;

    // ----- sequencingRules -----
    public List<SequencingRuleDefinition> PreConditionRuleList { get; set; } = new();
    public List<SequencingRuleDefinition> ExitConditionRuleList { get; set; } = new();
    public List<SequencingRuleDefinition> PostConditionRuleList { get; set; } = new();

    // ----- limitConditions -----
    public int? AttemptLimit { get; set; }
    public string? AttemptAbsoluteDurationLimit { get; set; }

    /// <summary>beginTimeLimit / endTimeLimit — khoảng thời gian được phép bắt đầu lượt thử (phần TÙY CHỌN của
    /// limitConditions; hệ thống hỗ trợ để làm lịch mở / hạn chót của từng mục trong khóa học).</summary>
    public DateTime? BeginTimeLimit { get; set; }
    public DateTime? EndTimeLimit { get; set; }

    // ----- rollupRules -----
    public bool RollupObjectiveSatisfied { get; set; } = true;
    public bool RollupProgressCompletion { get; set; } = true;
    public double ObjectiveMeasureWeight { get; set; } = 1.0;
    public List<RollupRuleDefinition> RollupRuleList { get; set; } = new();

    // ----- adlseq:rollupConsiderations -----
    public string RequiredForSatisfied { get; set; } = "always";
    public string RequiredForNotSatisfied { get; set; } = "always";
    public string RequiredForCompleted { get; set; } = "always";
    public string RequiredForIncomplete { get; set; } = "always";
    public bool MeasureSatisfactionIfActive { get; set; } = true;

    // ----- objectives (mục tiêu chính đứng đầu danh sách) -----
    public List<ObjectiveDefinition> ObjectiveList { get; set; } = new();

    // ----- randomizationControls -----
    public string RandomizationTiming { get; set; } = "never";
    public int? SelectCount { get; set; }
    public bool ReorderChildren { get; set; }
    public string SelectionTiming { get; set; } = "never";

    // ----- deliveryControls -----
    public bool Tracked { get; set; } = true;
    public bool CompletionSetByContent { get; set; }
    public bool ObjectiveSetByContent { get; set; }

    // ----- adlseq:constrainedChoiceConsiderations -----
    public bool PreventActivation { get; set; }
    public bool ConstrainChoice { get; set; }

    public ObjectiveDefinition? PrimaryObjective => ObjectiveList.FirstOrDefault(x => x.IsPrimary);
}

public sealed class SequencingRuleDefinition
{
    /// <summary>"all" | "any".</summary>
    public string ConditionCombination { get; set; } = "all";
    public List<RuleConditionDefinition> ConditionList { get; set; } = new();

    /// <summary>Tiền điều kiện: skip, disabled, hiddenFromChoice, stopForwardTraversal.
    /// Điều kiện thoát: exit. Hậu điều kiện: exitParent, exitAll, retry, retryAll, continue, previous.</summary>
    public string Action { get; set; } = "";
}

public sealed class RuleConditionDefinition
{
    /// <summary>satisfied, objectiveStatusKnown, objectiveMeasureKnown, objectiveMeasureGreaterThan,
    /// objectiveMeasureLessThan, completed, activityProgressKnown, attempted, attemptLimitExceeded,
    /// timeLimitExceeded, outsideAvailableTimeRange, always.</summary>
    public string Condition { get; set; } = "";
    public string? ReferencedObjective { get; set; }
    public double MeasureThreshold { get; set; }

    /// <summary>"noOp" | "not".</summary>
    public string Operator { get; set; } = "noOp";
}

public sealed class RollupRuleDefinition
{
    /// <summary>all | any | none | atLeastCount | atLeastPercent.</summary>
    public string ChildActivitySet { get; set; } = "all";
    public int MinimumCount { get; set; }
    public double MinimumPercent { get; set; }

    /// <summary>Luật rollup mặc định là "any" (khác luật sequencing mặc định "all").</summary>
    public string ConditionCombination { get; set; } = "any";
    public List<RollupConditionDefinition> ConditionList { get; set; } = new();

    /// <summary>satisfied | notSatisfied | completed | incomplete.</summary>
    public string Action { get; set; } = "";
}

public sealed class RollupConditionDefinition
{
    /// <summary>satisfied, objectiveStatusKnown, objectiveMeasureKnown, completed, activityProgressKnown,
    /// attempted, attemptLimitExceeded, timeLimitExceeded, outsideAvailableTimeRange.</summary>
    public string Condition { get; set; } = "";
    public string Operator { get; set; } = "noOp";
}

public sealed class ObjectiveDefinition
{
    /// <summary>objectiveID — mục tiêu chính được phép để trống khi không ánh xạ ra mục tiêu toàn cục.</summary>
    public string? Id { get; set; }
    public bool IsPrimary { get; set; }
    public bool SatisfiedByMeasure { get; set; }
    public double MinNormalizedMeasure { get; set; } = 1.0;
    public List<ObjectiveMapDefinition> MapList { get; set; } = new();

    /// <summary>Khóa dùng trong trạng thái — mục tiêu chính không tên vẫn cần một khóa ổn định.</summary>
    public string Key => string.IsNullOrEmpty(Id) ? (IsPrimary ? "#primary" : "#unnamed") : Id;
}

/// <summary>imsss:mapInfo, gộp thêm adlseq:mapInfo (4th Ed) của cùng mục tiêu toàn cục.</summary>
public sealed class ObjectiveMapDefinition
{
    public string TargetObjectiveId { get; set; } = "";
    public bool ReadSatisfiedStatus { get; set; } = true;
    public bool ReadNormalizedMeasure { get; set; } = true;
    public bool WriteSatisfiedStatus { get; set; }
    public bool WriteNormalizedMeasure { get; set; }

    public bool ReadRawScore { get; set; } = true;
    public bool ReadMinScore { get; set; } = true;
    public bool ReadMaxScore { get; set; } = true;
    public bool ReadCompletionStatus { get; set; } = true;
    public bool ReadProgressMeasure { get; set; } = true;
    public bool WriteRawScore { get; set; }
    public bool WriteMinScore { get; set; }
    public bool WriteMaxScore { get; set; }
    public bool WriteCompletionStatus { get; set; }
    public bool WriteProgressMeasure { get; set; }
}
