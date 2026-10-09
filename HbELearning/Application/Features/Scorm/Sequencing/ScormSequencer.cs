// File: Application/Features/Scorm/Sequencing/ScormSequencer.cs
using System.Text.Json;
using HbELearning.Application.Features.Scorm.Model;
using HbELearning.Application.Features.Scorm.Runtime;

namespace HbELearning.Application.Features.Scorm.Sequencing;

/// <summary>Yêu cầu điều hướng (navigation request) — từ nút của LMS hoặc từ adl.nav.request của SCO.</summary>
public sealed record ScormNavigationRequest(string Type, string? TargetActivityId = null)
{
    public static readonly IReadOnlySet<string> TypeSet = new HashSet<string>(StringComparer.Ordinal)
    {
        "start", "resumeAll", "continue", "previous", "choice", "jump",
        "exit", "exitAll", "abandon", "abandonAll", "suspendAll",
    };
}

public sealed class ScormSequencingResult
{
    /// <summary>Yêu cầu điều hướng có hợp lệ hay không (NB.2.1). Hợp lệ mà vẫn không giao được hoạt động nào thì
    /// xem <see cref="ExceptionCode"/>.</summary>
    public bool IsValid { get; init; } = true;

    /// <summary>Mã ngoại lệ của chuẩn (VD "SB.2.2-2") — null nếu mọi bước chạy trơn.</summary>
    public string? ExceptionCode { get; init; }

    /// <summary>Hoạt động lá vừa được giao để phát.</summary>
    public string? DeliveredActivityId { get; init; }

    /// <summary>Hoạt động được giao là học tiếp một lượt thử đang tạm dừng (cmi.entry = "resume").</summary>
    public bool IsResumedDelivery { get; init; }

    /// <summary>Phiên sequencing đã kết thúc (thoát hết, đi quá hoạt động cuối, tạm dừng tất cả...).</summary>
    public bool EndSequencingSession { get; init; }
}

/// <summary>Tình trạng một hoạt động để vẽ mục lục.</summary>
public sealed record ScormActivityStatus(
    string Id, string Title, int Depth, bool IsLeaf, bool IsVisible,
    string Completion, string Success, double? ScoreScaled, int AttemptCount,
    bool IsActive, bool IsSuspended, bool IsCurrent,
    bool CanChoose, bool IsHiddenFromChoice, bool IsDisabled);

/// <summary>Trạng thái có thẩm quyền của một hoạt động lá (xem ScormSequencer.ApplyAuthoritativeStatus). null =
/// chưa biết.</summary>
public sealed record ScormAuthoritativeStatus(bool Attempted, bool? Completed, bool? Satisfied, double? Measure);

/// <summary>Tóm tắt một hoạt động. Completed / Satisfied null = chưa biết.</summary>
public sealed record ScormActivitySummary(bool? Completed, bool? Satisfied, double? Measure, int AttemptCount,
    bool EverCompleted, bool EverSatisfied, bool EverFailed, double? BestMeasure);

public sealed class ScormNavigationAvailability
{
    public bool CanContinue { get; init; }
    public bool CanPrevious { get; init; }
    public HashSet<string> ChoiceValidIdSet { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> JumpValidIdSet { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Engine Sequencing &amp; Navigation của SCORM 2004 4th Edition.
///
/// Mỗi hàm tương ứng một quy trình trong pseudo-code chuẩn (Phụ lục C cuốn SN) và mang đúng mã của nó trong chú
/// thích — OP.1 Overall Sequencing, NB.2.1 Navigation Request, TB.2.3 Termination Request, SB.2.x Sequencing
/// Request, DB.x Delivery, UP.x tiện ích, RB.1.x Rollup. Giữ đúng tên và thứ tự các bước để người sau đối chiếu
/// từng dòng với chuẩn được; chỗ nào làm khác chuẩn đều ghi rõ lý do.
///
/// Engine không đụng database: nhận cây định nghĩa + trạng thái, biến đổi trạng thái tại chỗ. Lưu lại là việc của
/// ScormService.
/// </summary>
public sealed class ScormSequencer
{
    private const string Forward = "forward";
    private const string Backward = "backward";

    private readonly ScormActivityTree _tree;
    private readonly TreeIndex _index;

    public ScormSequencingState State { get; }

    /// <summary>Chạy sau mỗi lần rollup. LMS dùng để tự duy trì những mục tiêu toàn cục mà giá trị của chúng do LMS
    /// quyết định chứ không do nội dung báo (VD mục tiêu "đã xong mục trước" của chuỗi học tuần tự — tính theo dữ
    /// liệu chấm trên máy chủ, học viên không giả được). Không có trong chuẩn; null = engine chạy đúng như chuẩn.</summary>
    public Action<ScormSequencer>? AfterRollup { get; init; }

    public ScormSequencer(ScormActivityTree tree, ScormSequencingState state)
        : this(tree, state, new TreeIndex(tree)) { }

    private ScormSequencer(ScormActivityTree tree, ScormSequencingState state, TreeIndex index)
    {
        _tree = tree;
        _index = index;
        State = state;
        foreach (var activity in index.Order) EnsureState(activity);
    }

    // ==================== CHỈ MỤC CÂY ====================

    /// <summary>Bảng tra dựng một lần cho mỗi cây: id -> hoạt động, cha, thứ tự tiền thứ tự, luật rollup hiệu lực.</summary>
    private sealed class TreeIndex
    {
        public Dictionary<string, ScormActivityDefinition> ById { get; } = new(StringComparer.Ordinal);
        public Dictionary<ScormActivityDefinition, ScormActivityDefinition?> ParentOf { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<ScormActivityDefinition, int> PreorderOf { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<ScormActivityDefinition, int> DepthOf { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<ScormActivityDefinition, List<RollupRuleDefinition>> RollupRuleOf { get; } = new(ReferenceEqualityComparer.Instance);
        public List<ScormActivityDefinition> Order { get; } = new();

        public TreeIndex(ScormActivityTree tree)
        {
            void Walk(ScormActivityDefinition activity, ScormActivityDefinition? parent, int depth)
            {
                // Mọi hoạt động đều có một mục tiêu chính (SN 4.2.1.1) — manifest không khai thì dùng mục tiêu
                // mặc định không tên, không ánh xạ.
                if (activity.Sequencing.PrimaryObjective is null)
                    activity.Sequencing.ObjectiveList.Insert(0, new ObjectiveDefinition { IsPrimary = true });

                ById[activity.Id] = activity;
                ParentOf[activity] = parent;
                PreorderOf[activity] = Order.Count;
                DepthOf[activity] = depth;
                RollupRuleOf[activity] = BuildEffectiveRollupRules(activity.Sequencing);
                Order.Add(activity);
                foreach (var child in activity.Children) Walk(child, activity, depth + 1);
            }
            Walk(tree.Root, null, 0);
        }

        /// <summary>Luật rollup của hoạt động + luật MẶC ĐỊNH của chuẩn (SN 4.6.5) cho nhóm hành động nào chưa được
        /// khai: đạt khi mọi con đạt, không đạt khi mọi con đã biết trạng thái; hoàn thành khi mọi con hoàn thành,
        /// chưa hoàn thành khi mọi con đã biết tiến độ.</summary>
        private static List<RollupRuleDefinition> BuildEffectiveRollupRules(SequencingDefinition sequencing)
        {
            var list = new List<RollupRuleDefinition>(sequencing.RollupRuleList);
            if (!list.Any(r => r.Action is "satisfied" or "notSatisfied"))
            {
                list.Add(DefaultRule("satisfied", "satisfied"));
                list.Add(DefaultRule("objectiveStatusKnown", "notSatisfied"));
            }
            if (!list.Any(r => r.Action is "completed" or "incomplete"))
            {
                list.Add(DefaultRule("completed", "completed"));
                list.Add(DefaultRule("activityProgressKnown", "incomplete"));
            }
            return list;

            static RollupRuleDefinition DefaultRule(string condition, string action) => new()
            {
                ChildActivitySet = "all",
                ConditionCombination = "any",
                ConditionList = { new RollupConditionDefinition { Condition = condition } },
                Action = action,
            };
        }
    }

    private ScormActivityDefinition Root => _tree.Root;

    private ScormActivityDefinition? Find(string? id)
        => id is not null && _index.ById.TryGetValue(id, out var activity) ? activity : null;

    private ScormActivityDefinition? Current
    {
        get => Find(State.CurrentActivityId);
        set => State.CurrentActivityId = value?.Id;
    }

    private ScormActivityDefinition? Suspended
    {
        get => Find(State.SuspendedActivityId);
        set => State.SuspendedActivityId = value?.Id;
    }

    private ScormActivityDefinition? Parent(ScormActivityDefinition activity) => _index.ParentOf[activity];

    private ActivityState S(ScormActivityDefinition activity) => EnsureState(activity);

    private ActivityState EnsureState(ScormActivityDefinition activity)
    {
        if (State.ActivityById.TryGetValue(activity.Id, out var state)) return state;

        state = new ActivityState();
        State.ActivityById[activity.Id] = state;
        // Chọn lọc / xáo trộn "once" chạy đúng một lần — lúc hoạt động mới xuất hiện trong trạng thái.
        if (!activity.IsLeaf) ApplySelectionAndRandomization(activity, state, isNewAttempt: false);
        return state;
    }

    /// <summary>Available Children (SN 4.7 Selection and Randomization).</summary>
    private List<ScormActivityDefinition> Available(ScormActivityDefinition activity)
    {
        var idList = S(activity).AvailableChildIdList;
        if (idList is null) return activity.Children;
        var list = new List<ScormActivityDefinition>(idList.Count);
        foreach (var id in idList)
            if (Find(id) is ScormActivityDefinition child && Parent(child) == activity) list.Add(child);
        return list;
    }

    private bool IsInAvailable(ScormActivityDefinition activity)
        => Parent(activity) is not ScormActivityDefinition parent || Available(parent).Contains(activity);

    /// <summary>Từ activity lên tới gốc, gồm cả hai đầu (activity đứng đầu).</summary>
    private List<ScormActivityDefinition> PathToRoot(ScormActivityDefinition activity)
    {
        var list = new List<ScormActivityDefinition>();
        for (var walker = activity; walker is not null; walker = Parent(walker)) list.Add(walker);
        return list;
    }

    /// <summary>Từ gốc xuống tới activity, gồm cả hai đầu.</summary>
    private List<ScormActivityDefinition> PathFromRoot(ScormActivityDefinition activity)
    {
        var list = PathToRoot(activity);
        list.Reverse();
        return list;
    }

    /// <summary>Từ <paramref name="from"/> đi lên tới <paramref name="ancestor"/>, KHÔNG gồm ancestor.</summary>
    private List<ScormActivityDefinition> PathUpExclusive(ScormActivityDefinition from, ScormActivityDefinition ancestor)
    {
        var list = new List<ScormActivityDefinition>();
        for (var walker = from; walker is not null && walker != ancestor; walker = Parent(walker)) list.Add(walker);
        return list;
    }

    private ScormActivityDefinition CommonAncestor(ScormActivityDefinition a, ScormActivityDefinition b)
    {
        var ancestorSet = new HashSet<ScormActivityDefinition>(PathToRoot(a), ReferenceEqualityComparer.Instance);
        for (var walker = b; walker is not null; walker = Parent(walker))
            if (ancestorSet.Contains(walker)) return walker;
        return Root;
    }

    private bool IsDescendantOf(ScormActivityDefinition activity, ScormActivityDefinition ancestor)
    {
        for (var walker = Parent(activity); walker is not null; walker = Parent(walker))
            if (walker == ancestor) return true;
        return false;
    }

    private bool IsSibling(ScormActivityDefinition a, ScormActivityDefinition b)
        => a != b && Parent(a) is not null && Parent(a) == Parent(b);

    /// <summary>a đứng SAU b trong lượt duyệt tiền thứ tự.</summary>
    private bool IsForwardOf(ScormActivityDefinition a, ScormActivityDefinition b) => _index.PreorderOf[a] > _index.PreorderOf[b];

    private bool IsLastInForwardTraversal(ScormActivityDefinition activity)
    {
        var last = Root;
        while (Available(last) is { Count: > 0 } children) last = children[^1];
        return last == activity;
    }

    private IEnumerable<ScormActivityDefinition> Descendants(ScormActivityDefinition activity)
    {
        foreach (var child in activity.Children)
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    // ==================== MỤC TIÊU: ĐỌC / GHI QUA ÁNH XẠ TOÀN CỤC ====================

    private static ObjectiveDefinition PrimaryOf(ScormActivityDefinition activity) => activity.Sequencing.PrimaryObjective!;

    private ObjectiveState Local(ScormActivityDefinition activity, ObjectiveDefinition objective)
    {
        var map = S(activity).ObjectiveByKey;
        if (!map.TryGetValue(objective.Key, out var state)) map[objective.Key] = state = new ObjectiveState();
        return state;
    }

    private ObjectiveState Global(string targetId)
    {
        if (!State.GlobalObjectiveById.TryGetValue(targetId, out var state))
            State.GlobalObjectiveById[targetId] = state = new ObjectiveState();
        return state;
    }

    /// <summary>Trạng thái đạt hiệu lực: đọc từ mục tiêu toàn cục (readSatisfiedStatus) nếu ở đó đã biết, không thì
    /// dùng bản cục bộ. null = chưa biết.</summary>
    private bool? ObjectiveSatisfied(ScormActivityDefinition activity, ObjectiveDefinition objective)
    {
        foreach (var map in objective.MapList)
            if (map.ReadSatisfiedStatus && State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global) && global.ProgressStatus)
                return global.SatisfiedStatus;
        var local = Local(activity, objective);
        return local.ProgressStatus ? local.SatisfiedStatus : null;
    }

    private double? ObjectiveMeasure(ScormActivityDefinition activity, ObjectiveDefinition objective)
    {
        foreach (var map in objective.MapList)
            if (map.ReadNormalizedMeasure && State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global) && global.MeasureStatus)
                return global.NormalizedMeasure;
        var local = Local(activity, objective);
        return local.MeasureStatus ? local.NormalizedMeasure : null;
    }

    private static bool IsKnownCompletion(string? value) => value is "completed" or "incomplete" or "not attempted";

    /// <summary>Trạng thái hoàn thành hiệu lực của MỘT HOẠT ĐỘNG (4th Ed): mục tiêu chính của nó đọc hoàn thành từ mục
    /// tiêu toàn cục (adlseq readCompletionStatus) và ở đó đã biết thì lấy ở đó, không thì lấy lượt thử của chính nó.</summary>
    private (bool Known, bool Completed) ActivityCompletion(ScormActivityDefinition activity)
    {
        foreach (var map in PrimaryOf(activity).MapList)
            if (map.ReadCompletionStatus && State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global)
                && IsKnownCompletion(global.CompletionStatus))
                return (true, global.CompletionStatus == "completed");
        var state = S(activity);
        return state.AttemptProgressStatus ? (true, state.AttemptCompletionStatus) : (false, false);
    }

    /// <summary>Trạng thái hoàn thành của MỘT MỤC TIÊU (4th Ed) — luật "completed" / "activityProgressKnown" có
    /// referencedObjective thì xét mục tiêu đó chứ không xét hoạt động.</summary>
    private (bool Known, bool Completed) ObjectiveCompletion(ScormActivityDefinition activity, ObjectiveDefinition objective)
    {
        if (objective.IsPrimary) return ActivityCompletion(activity);
        foreach (var map in objective.MapList)
            if (map.ReadCompletionStatus && State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global)
                && IsKnownCompletion(global.CompletionStatus))
                return (true, global.CompletionStatus == "completed");
        var local = Local(activity, objective);
        return IsKnownCompletion(local.CompletionStatus) ? (true, local.CompletionStatus == "completed") : (false, false);
    }

    /// <summary>Đẩy hoàn thành / progress measure của lượt thử hiện tại ra các mục tiêu toàn cục mà mục tiêu chính được
    /// phép ghi (adlseq writeCompletionStatus / writeProgressMeasure). Chạy sau mỗi rollup để cả trạng thái do End
    /// Attempt điền mặc định hay do rollup tính ra cũng tới được các hoạt động khác đang đọc.</summary>
    private void WriteActivityCompletionMaps(ScormActivityDefinition activity)
    {
        var state = S(activity);
        foreach (var map in PrimaryOf(activity).MapList)
        {
            if (!map.WriteCompletionStatus && !map.WriteProgressMeasure) continue;
            var global = Global(map.TargetObjectiveId);
            if (map.WriteCompletionStatus && state.AttemptProgressStatus)
                global.CompletionStatus = state.AttemptCompletionStatus ? "completed" : "incomplete";
            if (map.WriteProgressMeasure && state.AttemptCompletionAmountStatus)
                global.ProgressMeasure = state.AttemptCompletionAmount;
        }
    }

    /// <summary>Ghi trạng thái đạt. Chỉ GIÁ TRỊ ĐÃ BIẾT mới được đẩy lên mục tiêu toàn cục: một lượt thử mới xóa trạng
    /// thái cục bộ về "chưa biết", nếu ghi cả cái "chưa biết" đó lên thì kết quả bài kiểm tra đầu vào (thứ các
    /// luật remediation đọc) bị xóa theo mỗi lần học viên làm lại một bài khác.</summary>
    private void SetObjectiveSatisfied(ScormActivityDefinition activity, ObjectiveDefinition objective, bool? value)
    {
        var local = Local(activity, objective);
        local.ProgressStatus = value.HasValue;
        local.SatisfiedStatus = value ?? false;
        if (!value.HasValue) return;
        foreach (var map in objective.MapList.Where(m => m.WriteSatisfiedStatus))
        {
            var global = Global(map.TargetObjectiveId);
            global.ProgressStatus = true;
            global.SatisfiedStatus = value.Value;
        }
    }

    private void SetObjectiveMeasure(ScormActivityDefinition activity, ObjectiveDefinition objective, double? value)
    {
        var local = Local(activity, objective);
        local.MeasureStatus = value.HasValue;
        local.NormalizedMeasure = value ?? 0;
        if (!value.HasValue) return;
        foreach (var map in objective.MapList.Where(m => m.WriteNormalizedMeasure))
        {
            var global = Global(map.TargetObjectiveId);
            global.MeasureStatus = true;
            global.NormalizedMeasure = value.Value;
        }
    }

    /// <summary>Phần mở rộng 4th Ed: điểm thô / min / max, trạng thái hoàn thành, progress measure của mục tiêu.</summary>
    private void SetObjectiveExtended(ScormActivityDefinition activity, ObjectiveDefinition objective,
        double? raw, double? min, double? max, string? completion, double? progress)
    {
        var local = Local(activity, objective);
        local.RawScore = raw;
        local.MinScore = min;
        local.MaxScore = max;
        local.CompletionStatus = completion is "completed" or "incomplete" or "not attempted" ? completion : null;
        local.ProgressMeasure = progress;

        foreach (var map in objective.MapList)
        {
            if (!(map.WriteRawScore || map.WriteMinScore || map.WriteMaxScore || map.WriteCompletionStatus || map.WriteProgressMeasure)) continue;
            var global = Global(map.TargetObjectiveId);
            if (map.WriteRawScore && raw.HasValue) global.RawScore = raw;
            if (map.WriteMinScore && min.HasValue) global.MinScore = min;
            if (map.WriteMaxScore && max.HasValue) global.MaxScore = max;
            if (map.WriteCompletionStatus && local.CompletionStatus is not null) global.CompletionStatus = local.CompletionStatus;
            if (map.WriteProgressMeasure && progress.HasValue) global.ProgressMeasure = progress;
        }
    }

    // ==================== OP.1 OVERALL SEQUENCING PROCESS ====================

    /// <summary>OP.1 — xử lý trọn một yêu cầu điều hướng: kết thúc lượt đang chạy (nếu cần), chọn hoạt động kế
    /// tiếp, giao nó. Biến đổi <see cref="State"/> tại chỗ.</summary>
    public ScormSequencingResult Navigate(ScormNavigationRequest request)
    {
        var navigation = NavigationRequestProcess(request);
        if (!navigation.Valid) return new ScormSequencingResult { IsValid = false, ExceptionCode = navigation.Exception };

        var sequencingRequest = navigation.Sequencing;
        var target = navigation.Target;

        if (navigation.Termination is not null)
        {
            var termination = TerminationRequestProcess(navigation.Termination);
            if (!termination.Valid) return new ScormSequencingResult { IsValid = false, ExceptionCode = termination.Exception };
            if (termination.Sequencing is not null)
            {
                // Hậu điều kiện (retry, continue, previous...) thay cho yêu cầu ban đầu — đúng OP.1 bước 1.4.
                sequencingRequest = termination.Sequencing;
                if (sequencingRequest is not ("choice" or "jump")) target = null;
            }
        }

        if (sequencingRequest is null) return new ScormSequencingResult();

        var sequencing = SequencingRequestProcess(sequencingRequest, target);
        if (sequencing.EndSession)
        {
            EndSequencingSession();
            return new ScormSequencingResult { EndSequencingSession = true, ExceptionCode = sequencing.Exception };
        }
        if (!sequencing.Valid || sequencing.Delivery is null)
            return new ScormSequencingResult { ExceptionCode = sequencing.Exception };

        var delivery = DeliveryRequestProcess(sequencing.Delivery);
        if (!delivery.Valid) return new ScormSequencingResult { ExceptionCode = delivery.Exception };

        var (delivered, resumed, exception) = ContentDeliveryEnvironmentProcess(sequencing.Delivery);
        if (!delivered) return new ScormSequencingResult { ExceptionCode = exception };

        return new ScormSequencingResult { DeliveredActivityId = sequencing.Delivery.Id, IsResumedDelivery = resumed };
    }

    /// <summary>Kết thúc phiên sequencing: không còn Current Activity. Cố ý KHÔNG kết thúc lượt thử của hoạt động gốc
    /// — chỉ Exit All mới làm việc đó; phiên sau "Start" sẽ học tiếp trong cùng lượt thử gốc, giữ nguyên tiến độ
    /// các hoạt động đã xong.</summary>
    private void EndSequencingSession()
    {
        foreach (var activity in _index.Order)
            if (activity != Root) S(activity).IsActive = false;
        Current = null;
    }

    /// <summary>Học lại từ đầu: bỏ mọi trạng thái tạm dừng và kết thúc lượt thử gốc đang dở, để lần Start kế tiếp mở
    /// một lượt thử MỚI trên toàn bộ cây (tiến độ của lượt cũ bị xóa theo useCurrentAttempt*Info).</summary>
    public void ResetForNewAttempt()
    {
        foreach (var activity in _index.Order)
        {
            var state = S(activity);
            state.IsActive = false;
            state.IsSuspended = false;
        }
        Suspended = null;
        Current = null;
        State.LastDeliveredActivityId = null;

        // Gói khai mục tiêu / dữ liệu chia sẻ KHÔNG toàn hệ thống (objectivesGlobalToSystem / sharedDataGlobalToSystem
        // = false) nghĩa là chúng chỉ sống trong một lượt học gói — lượt mới phải bắt đầu sạch, nếu không kết quả cũ
        // vẫn mở khóa sẵn các mục mà lượt mới chưa học.
        if (!_tree.ObjectivesGlobalToSystem) State.GlobalObjectiveById.Clear();
        if (!_tree.SharedDataGlobalToSystem) State.SharedDataById.Clear();
    }

    // ==================== NB.2.1 NAVIGATION REQUEST PROCESS ====================

    private readonly record struct NavigationOutcome(bool Valid, string? Termination, string? Sequencing,
        ScormActivityDefinition? Target, string? Exception);

    private static NavigationOutcome NavInvalid(string code) => new(false, null, null, null, code);

    private NavigationOutcome NavigationRequestProcess(ScormNavigationRequest request)
    {
        var current = Current;
        switch (request.Type)
        {
            case "start":
                return current is null ? new(true, null, "start", null, null) : NavInvalid("NB.2.1-1");

            case "resumeAll":
                if (current is not null) return NavInvalid("NB.2.1-1");
                return Suspended is not null ? new(true, null, "resumeAll", null, null) : NavInvalid("NB.2.1-3");

            case "continue":
                if (current is null) return NavInvalid("NB.2.1-2");
                if (current != Root && Parent(current)!.Sequencing.ControlFlow)
                    return S(current).IsActive ? new(true, "exit", "continue", null, null) : new(true, null, "continue", null, null);
                return NavInvalid("NB.2.1-4");

            case "previous":
                if (current is null) return NavInvalid("NB.2.1-2");
                if (current == Root) return NavInvalid("NB.2.1-6");
                var parent = Parent(current)!;
                if (parent.Sequencing.ControlFlow && !parent.Sequencing.ControlForwardOnly)
                    return S(current).IsActive ? new(true, "exit", "previous", null, null) : new(true, null, "previous", null, null);
                return NavInvalid("NB.2.1-5");

            case "choice":
            {
                var target = Find(request.TargetActivityId);
                if (target is null || !IsInAvailable(target)) return NavInvalid("NB.2.1-11");
                if (target != Root && !Parent(target)!.Sequencing.ControlChoice) return NavInvalid("NB.2.1-10");
                if (current is null) return new(true, null, "choice", target, null);

                var common = CommonAncestor(current, target);
                foreach (var activity in PathUpExclusive(current, common))
                    if (S(activity).IsActive && !activity.Sequencing.ControlChoiceExit) return NavInvalid("NB.2.1-8");

                return S(current).IsActive ? new(true, "exit", "choice", target, null) : new(true, null, "choice", target, null);
            }

            case "jump":
            {
                var target = Find(request.TargetActivityId);
                if (target is null || !IsInAvailable(target)) return NavInvalid("NB.2.1-11");
                return current is not null && S(current).IsActive
                    ? new(true, "exit", "jump", target, null)
                    : new(true, null, "jump", target, null);
            }

            case "exit":
                if (current is null) return NavInvalid("NB.2.1-2");
                return S(current).IsActive ? new(true, "exit", "exit", null, null) : NavInvalid("NB.2.1-12");

            case "exitAll":
                return current is null ? NavInvalid("NB.2.1-2") : new(true, "exitAll", "exit", null, null);

            case "abandon":
                if (current is null) return NavInvalid("NB.2.1-2");
                return S(current).IsActive ? new(true, "abandon", "exit", null, null) : NavInvalid("NB.2.1-12");

            case "abandonAll":
                return current is null ? NavInvalid("NB.2.1-2") : new(true, "abandonAll", "exit", null, null);

            case "suspendAll":
                return current is null ? NavInvalid("NB.2.1-2") : new(true, "suspendAll", "exit", null, null);

            default:
                return NavInvalid("NB.2.1-13");
        }
    }

    // ==================== TB.2.3 TERMINATION REQUEST PROCESS ====================

    private readonly record struct TerminationOutcome(bool Valid, string? Sequencing, string? Exception);

    private TerminationOutcome TerminationRequestProcess(string request)
    {
        var current = Current;
        if (current is null) return new(false, null, "TB.2.3-1");
        if (request is "exit" or "abandon" && !S(current).IsActive) return new(false, null, "TB.2.3-2");

        string? carriedSequencing = null;
        switch (request)
        {
            case "exit":
            {
                EndAttemptProcess(current);
                SequencingExitActionRulesSubprocess();

                bool processedExit;
                string? postSequencing;
                do
                {
                    processedExit = false;
                    var (postTermination, sequencing) = SequencingPostConditionRulesSubprocess();
                    postSequencing = sequencing;

                    if (postTermination == "exitAll")
                    {
                        carriedSequencing = sequencing;
                        goto case "exitAll";
                    }
                    if (postTermination == "exitParent")
                    {
                        if (Current != Root)
                        {
                            Current = Parent(Current!);
                            EndAttemptProcess(Current!);
                            processedExit = true;
                        }
                        else return new(false, null, "TB.2.3-4");
                    }
                    else if (Current == Root && postSequencing != "retry")
                    {
                        return new(true, "exit", null);
                    }
                } while (processedExit);

                return new(true, postSequencing, null);
            }

            case "exitAll":
            {
                if (S(Current!).IsActive) EndAttemptProcess(Current!);
                TerminateDescendentAttemptsProcess(Root);
                EndAttemptProcess(Root);
                Current = Root;
                return new(true, carriedSequencing ?? "exit", null);
            }

            case "suspendAll":
            {
                var state = S(current);
                if (state.IsActive || state.IsSuspended)
                {
                    OverallRollupProcess(current);
                    Suspended = current;
                }
                else
                {
                    if (current == Root) return new(false, null, "TB.2.3-3");
                    Suspended = Parent(current);
                }

                foreach (var activity in PathToRoot(Suspended!))
                {
                    S(activity).IsActive = false;
                    S(activity).IsSuspended = true;
                }
                Current = Root;
                return new(true, "exit", null);
            }

            case "abandon":
                S(current).IsActive = false;
                return new(true, null, null);

            case "abandonAll":
                foreach (var activity in PathToRoot(current)) S(activity).IsActive = false;
                Current = Root;
                return new(true, "exit", null);

            default:
                return new(false, null, "TB.2.3-7");
        }
    }

    /// <summary>TB.2.1 — luật điều kiện thoát của các tổ tiên (từ gốc xuống cha của hoạt động hiện tại).</summary>
    private void SequencingExitActionRulesSubprocess()
    {
        var current = Current;
        if (current is null || Parent(current) is not ScormActivityDefinition parent) return;

        ScormActivityDefinition? exitTarget = null;
        foreach (var activity in PathFromRoot(parent))
        {
            if (SequencingRulesCheck(activity, activity.Sequencing.ExitConditionRuleList, "exit") is not null)
            {
                exitTarget = activity;
                break;
            }
        }

        if (exitTarget is null) return;
        TerminateDescendentAttemptsProcess(exitTarget);
        EndAttemptProcess(exitTarget);
        Current = exitTarget;
    }

    /// <summary>TB.2.2 — luật hậu điều kiện của hoạt động hiện tại.</summary>
    private (string? Termination, string? Sequencing) SequencingPostConditionRulesSubprocess()
    {
        var current = Current!;
        if (S(current).IsSuspended) return (null, null);

        var action = SequencingRulesCheck(current, current.Sequencing.PostConditionRuleList,
            "exitParent", "exitAll", "retry", "retryAll", "continue", "previous");
        return action switch
        {
            "retry" or "continue" or "previous" => (null, action),
            "exitParent" or "exitAll" => (action, null),
            "retryAll" => ("exitAll", "retry"),
            _ => (null, null),
        };
    }

    // ==================== SB.2.12 SEQUENCING REQUEST PROCESS ====================

    private readonly record struct SequencingOutcome(bool Valid, ScormActivityDefinition? Delivery, bool EndSession, string? Exception);

    private static SequencingOutcome SeqInvalid(string code) => new(false, null, false, code);

    private SequencingOutcome SequencingRequestProcess(string request, ScormActivityDefinition? target) => request switch
    {
        "start" => StartSequencingRequestProcess(),
        "resumeAll" => ResumeAllSequencingRequestProcess(),
        "exit" => ExitSequencingRequestProcess(),
        "retry" => RetrySequencingRequestProcess(),
        "continue" => ContinueSequencingRequestProcess(),
        "previous" => PreviousSequencingRequestProcess(),
        "choice" => ChoiceSequencingRequestProcess(target),
        "jump" => JumpSequencingRequestProcess(target),
        _ => SeqInvalid("SB.2.12-1"),
    };

    /// <summary>SB.2.5</summary>
    private SequencingOutcome StartSequencingRequestProcess()
    {
        if (Current is not null) return SeqInvalid("SB.2.5-1");
        if (Root.IsLeaf) return new(true, Root, false, null);

        var flow = FlowSubprocess(Root, Forward, considerChildren: true);
        return flow.Deliverable ? new(true, flow.Activity, false, null) : new(false, null, flow.EndSession, flow.Exception);
    }

    /// <summary>SB.2.6</summary>
    private SequencingOutcome ResumeAllSequencingRequestProcess()
    {
        if (Current is not null) return SeqInvalid("SB.2.6-1");
        if (Suspended is null) return SeqInvalid("SB.2.6-2");
        return new(true, Suspended, false, null);
    }

    /// <summary>SB.2.7</summary>
    private SequencingOutcome ContinueSequencingRequestProcess()
    {
        var current = Current;
        if (current is null) return SeqInvalid("SB.2.7-1");
        if (current != Root && !Parent(current)!.Sequencing.ControlFlow) return SeqInvalid("SB.2.7-2");

        var flow = FlowSubprocess(current, Forward, considerChildren: false);
        return flow.Deliverable ? new(true, flow.Activity, false, null) : new(false, null, flow.EndSession, flow.Exception);
    }

    /// <summary>SB.2.8</summary>
    private SequencingOutcome PreviousSequencingRequestProcess()
    {
        var current = Current;
        if (current is null) return SeqInvalid("SB.2.8-1");
        if (current != Root && !Parent(current)!.Sequencing.ControlFlow) return SeqInvalid("SB.2.8-2");

        var flow = FlowSubprocess(current, Backward, considerChildren: false);
        return flow.Deliverable ? new(true, flow.Activity, false, null) : new(false, null, false, flow.Exception);
    }

    /// <summary>SB.2.11</summary>
    private SequencingOutcome ExitSequencingRequestProcess()
    {
        var current = Current;
        if (current is null) return SeqInvalid("SB.2.11-1");
        if (S(current).IsActive) return SeqInvalid("SB.2.11-2");
        return current == Root ? new(true, null, true, null) : new(true, null, false, null);
    }

    /// <summary>SB.2.10</summary>
    private SequencingOutcome RetrySequencingRequestProcess()
    {
        var current = Current;
        if (current is null) return SeqInvalid("SB.2.10-1");
        if (S(current).IsActive || S(current).IsSuspended) return SeqInvalid("SB.2.10-2");

        if (!current.IsLeaf)
        {
            var flow = FlowSubprocess(current, Forward, considerChildren: true);
            return flow.Deliverable ? new(true, flow.Activity, false, null) : SeqInvalid("SB.2.10-3");
        }
        return new(true, current, false, null);
    }

    /// <summary>SB.2.13 (4th Ed) — nhảy thẳng tới một hoạt động, bỏ qua các ràng buộc điều hướng.</summary>
    private SequencingOutcome JumpSequencingRequestProcess(ScormActivityDefinition? target)
    {
        if (Current is null) return SeqInvalid("SB.2.13-1");
        if (target is null) return SeqInvalid("SB.2.13-2");
        if (target.IsLeaf) return new(true, target, false, null);

        var flow = FlowSubprocess(target, Forward, considerChildren: true);
        return flow.Deliverable ? new(true, flow.Activity, false, null) : SeqInvalid("SB.2.13-3");
    }

    /// <summary>SB.2.9</summary>
    private SequencingOutcome ChoiceSequencingRequestProcess(ScormActivityDefinition? target)
    {
        if (target is null) return SeqInvalid("SB.2.9-1");

        var pathFromRoot = PathFromRoot(target);
        foreach (var activity in pathFromRoot)
            if (activity != Root && !Available(Parent(activity)!).Contains(activity)) return SeqInvalid("SB.2.9-2");
        foreach (var activity in pathFromRoot)
            if (SequencingRulesCheck(activity, activity.Sequencing.PreConditionRuleList, "hiddenFromChoice") is not null)
                return SeqInvalid("SB.2.9-3");
        if (target != Root && !Parent(target)!.Sequencing.ControlChoice) return SeqInvalid("SB.2.9-4");

        var current = Current;
        var common = current is not null ? CommonAncestor(current, target) : Root;

        if (current is not null && current == target)
        {
            // Chọn lại đúng hoạt động đang đứng — không có gì phải duyệt.
        }
        else if (current is not null && IsSibling(current, target))
        {
            var siblingList = Available(Parent(current)!);
            var from = siblingList.IndexOf(current);
            var to = siblingList.IndexOf(target);
            if (from < 0 || to < 0) return SeqInvalid("SB.2.9-5");

            var direction = to > from ? Forward : Backward;
            var range = to > from ? siblingList.GetRange(from, to - from + 1) : siblingList.GetRange(to, from - to + 1);
            if (direction == Backward) range.Reverse();
            foreach (var activity in range)
            {
                var traversal = ChoiceActivityTraversalSubprocess(activity, direction);
                if (!traversal.Reachable) return SeqInvalid(traversal.Exception!);
            }
        }
        else if (current is null || current == common)
        {
            // Đích là con cháu của hoạt động hiện tại (hoặc chưa có hoạt động hiện tại).
            var path = PathFromRoot(target).SkipWhile(a => a != common).ToList();
            if (path.Count == 0) return SeqInvalid("SB.2.9-5");
            foreach (var activity in path)
            {
                var traversal = ChoiceActivityTraversalSubprocess(activity, Forward);
                if (!traversal.Reachable) return SeqInvalid(traversal.Exception!);
                if (!S(activity).IsActive && activity != common && activity.Sequencing.PreventActivation)
                    return SeqInvalid("SB.2.9-6");
            }
        }
        else if (target == common)
        {
            // Đích là tổ tiên của hoạt động hiện tại.
            var path = PathToRoot(current).TakeWhile(a => a != target).Append(target).ToList();
            if (path.Count == 0) return SeqInvalid("SB.2.9-5");
            for (var i = 0; i < path.Count - 1; i++)
                if (!path[i].Sequencing.ControlChoiceExit) return SeqInvalid("SB.2.9-7");
        }
        else
        {
            // Đích nằm ở nhánh khác của cây.
            var upPath = PathUpExclusive(current, common);
            if (upPath.Count == 0) return SeqInvalid("SB.2.9-5");

            ScormActivityDefinition? constrained = null;
            foreach (var activity in upPath)
            {
                if (!activity.Sequencing.ControlChoiceExit) return SeqInvalid("SB.2.9-7");
                if (constrained is null && activity.Sequencing.ConstrainChoice) constrained = activity;
            }

            if (constrained is not null)
            {
                var direction = IsForwardOf(target, constrained) ? Forward : Backward;
                var consider = ChoiceFlowSubprocess(constrained, direction);
                if (!IsDescendantOf(target, consider) && target != consider && target != constrained)
                    return SeqInvalid("SB.2.9-8");
            }

            var downPath = PathFromRoot(target).SkipWhile(a => a != common).ToList();
            if (downPath.Count == 0) return SeqInvalid("SB.2.9-5");

            if (IsForwardOf(target, current))
            {
                foreach (var activity in downPath)
                {
                    var traversal = ChoiceActivityTraversalSubprocess(activity, Forward);
                    if (!traversal.Reachable) return SeqInvalid(traversal.Exception!);
                    if (!S(activity).IsActive && activity != common && activity.Sequencing.PreventActivation)
                        return SeqInvalid("SB.2.9-6");
                }
            }
            else
            {
                foreach (var activity in downPath)
                    if (!S(activity).IsActive && activity != common && activity.Sequencing.PreventActivation)
                        return SeqInvalid("SB.2.9-6");
            }
        }

        if (target.IsLeaf) return new(true, target, false, null);

        var flow = FlowSubprocess(target, Forward, considerChildren: true);
        if (flow.Deliverable) return new(true, flow.Activity, false, null);

        // Chọn một cụm mà bên trong không có gì giao được: dọn các lượt thử dang dở rồi đứng tại cụm đó.
        TerminateDescendentAttemptsProcess(common);
        EndAttemptProcess(common);
        Current = target;
        return SeqInvalid("SB.2.9-9");
    }

    /// <summary>SB.2.4</summary>
    private (bool Reachable, string? Exception) ChoiceActivityTraversalSubprocess(ScormActivityDefinition activity, string direction)
    {
        if (direction == Forward)
        {
            if (SequencingRulesCheck(activity, activity.Sequencing.PreConditionRuleList, "stopForwardTraversal") is not null)
                return (false, "SB.2.4-1");
            return (true, null);
        }

        if (Parent(activity) is ScormActivityDefinition parent)
            return parent.Sequencing.ControlForwardOnly ? (false, "SB.2.4-2") : (true, null);
        return (false, "SB.2.4-3");
    }

    /// <summary>SB.2.9.1</summary>
    private ScormActivityDefinition ChoiceFlowSubprocess(ScormActivityDefinition activity, string direction)
        => ChoiceFlowTreeTraversalSubprocess(activity, direction) ?? activity;

    /// <summary>SB.2.9.2</summary>
    private ScormActivityDefinition? ChoiceFlowTreeTraversalSubprocess(ScormActivityDefinition activity, string direction)
    {
        if (direction == Forward)
        {
            if (IsLastInForwardTraversal(activity) || activity == Root) return null;
            var siblingList = Available(Parent(activity)!);
            var index = siblingList.IndexOf(activity);
            return index == siblingList.Count - 1 || index < 0
                ? ChoiceFlowTreeTraversalSubprocess(Parent(activity)!, Forward)
                : siblingList[index + 1];
        }

        if (activity == Root) return null;
        var list = Available(Parent(activity)!);
        var position = list.IndexOf(activity);
        return position <= 0 ? ChoiceFlowTreeTraversalSubprocess(Parent(activity)!, Backward) : list[position - 1];
    }

    // ==================== FLOW (SB.2.1 / SB.2.2 / SB.2.3) ====================

    private readonly record struct TreeTraversalOutcome(ScormActivityDefinition? Activity, string Direction, bool EndSession, string? Exception);

    private readonly record struct FlowOutcome(bool Deliverable, ScormActivityDefinition? Activity, bool EndSession, string? Exception);

    /// <summary>SB.2.1 Flow Tree Traversal Subprocess.</summary>
    private TreeTraversalOutcome FlowTreeTraversalSubprocess(ScormActivityDefinition activity, string direction,
        bool considerChildren, string? previousDirection)
    {
        var reversed = false;
        if (previousDirection == Backward && Parent(activity) is ScormActivityDefinition reverseParent)
        {
            var siblings = Available(reverseParent);
            if (siblings.Count > 0 && siblings[^1] == activity)
            {
                direction = Backward;
                activity = siblings[0];
                reversed = true;
            }
        }

        if (direction == Forward)
        {
            if (IsLastInForwardTraversal(activity) || (activity == Root && !considerChildren))
            {
                TerminateDescendentAttemptsProcess(Root);
                return new(null, direction, true, null);
            }

            if (activity.IsLeaf || !considerChildren)
            {
                var siblingList = Available(Parent(activity)!);
                var index = siblingList.IndexOf(activity);
                if (index < 0 || index == siblingList.Count - 1)
                    return FlowTreeTraversalSubprocess(Parent(activity)!, Forward, considerChildren: false, previousDirection: null);
                return new(siblingList[index + 1], direction, false, null);
            }

            var children = Available(activity);
            return children.Count > 0 ? new(children[0], direction, false, null) : new(null, direction, false, "SB.2.1-2");
        }

        // ----- Backward -----
        if (activity == Root) return new(null, direction, false, "SB.2.1-3");

        if (activity.IsLeaf || !considerChildren)
        {
            var siblingList = Available(Parent(activity)!);
            var index = siblingList.IndexOf(activity);
            // Đã đảo chiều (đang đứng ở con đầu tiên) thì cũng như đứng ở đầu danh sách: đi lên cha.
            if (reversed || index <= 0)
                return FlowTreeTraversalSubprocess(Parent(activity)!, Backward, considerChildren: false, previousDirection: null);
            return new(siblingList[index - 1], direction, false, null);
        }

        var childList = Available(activity);
        if (childList.Count == 0) return new(null, direction, false, "SB.2.1-2");
        return activity.Sequencing.ControlForwardOnly
            ? new(childList[0], Forward, false, null)
            : new(childList[^1], Backward, false, null);
    }

    /// <summary>SB.2.2 Flow Activity Traversal Subprocess.</summary>
    private FlowOutcome FlowActivityTraversalSubprocess(ScormActivityDefinition activity, string direction, string? previousDirection, int depth = 0)
    {
        // Chặn đệ quy vô tận trên cây hỏng (mọi hoạt động đều bị bỏ qua theo vòng).
        if (depth > _index.Order.Count * 4) return new(false, activity, false, "SB.2.2-1");

        if (Parent(activity) is ScormActivityDefinition parent && !parent.Sequencing.ControlFlow)
            return new(false, activity, false, "SB.2.2-1");

        if (SequencingRulesCheck(activity, activity.Sequencing.PreConditionRuleList, "skip") is not null)
        {
            var next = FlowTreeTraversalSubprocess(activity, direction, considerChildren: false, previousDirection);
            if (next.Activity is null) return new(false, activity, next.EndSession, next.Exception);

            return previousDirection == Backward && next.Direction == Backward
                ? FlowActivityTraversalSubprocess(next.Activity, next.Direction, null, depth + 1)
                : FlowActivityTraversalSubprocess(next.Activity, next.Direction, previousDirection, depth + 1);
        }

        if (CheckActivityProcess(activity)) return new(false, activity, false, "SB.2.2-2");

        if (!activity.IsLeaf)
        {
            var next = FlowTreeTraversalSubprocess(activity, direction, considerChildren: true, previousDirection: null);
            if (next.Activity is null) return new(false, activity, next.EndSession, next.Exception);

            return direction == Backward && next.Direction == Forward
                ? FlowActivityTraversalSubprocess(next.Activity, Forward, Backward, depth + 1)
                : FlowActivityTraversalSubprocess(next.Activity, direction, null, depth + 1);
        }

        return new(true, activity, false, null);
    }

    /// <summary>SB.2.3 Flow Subprocess.</summary>
    private FlowOutcome FlowSubprocess(ScormActivityDefinition activity, string direction, bool considerChildren)
    {
        var next = FlowTreeTraversalSubprocess(activity, direction, considerChildren, previousDirection: null);
        if (next.Activity is null) return new(false, activity, next.EndSession, next.Exception);
        return FlowActivityTraversalSubprocess(next.Activity, next.Direction, null);
    }

    // ==================== DB.1.1 / DB.2 DELIVERY ====================

    /// <summary>DB.1.1 Delivery Request Process.</summary>
    private (bool Valid, string? Exception) DeliveryRequestProcess(ScormActivityDefinition activity)
    {
        if (!activity.IsLeaf) return (false, "DB.1.1-1");
        foreach (var item in PathFromRoot(activity))
            if (CheckActivityProcess(item)) return (false, "DB.1.1-3");
        return (true, null);
    }

    /// <summary>DB.2 Content Delivery Environment Process.</summary>
    private (bool Delivered, bool Resumed, string? Exception) ContentDeliveryEnvironmentProcess(ScormActivityDefinition activity)
    {
        if (Current is ScormActivityDefinition current && S(current).IsActive) return (false, false, "DB.2-1");

        var resumed = activity.Sequencing.Tracked && S(activity).IsSuspended;

        if (activity != Suspended) ClearSuspendedActivitySubprocess(activity);
        TerminateDescendentAttemptsProcess(activity);

        foreach (var item in PathFromRoot(activity))
        {
            var state = S(item);
            if (state.IsActive) continue;

            if (item.Sequencing.Tracked)
            {
                if (state.IsSuspended)
                {
                    state.IsSuspended = false;
                }
                else
                {
                    state.AttemptCount++;
                    if (state.AttemptCount == 1) state.ProgressStatus = true;
                    InitializeNewAttempt(item, state);
                }
            }
            state.IsActive = true;
        }

        Current = activity;
        State.LastDeliveredActivityId = activity.Id;
        if (Suspended == activity || (Suspended is not null && !S(Suspended).IsSuspended)) Suspended = null;
        return (true, resumed, null);
    }

    /// <summary>Khởi tạo thông tin tiến độ và mục tiêu cho lượt thử mới (DB.2 bước 5.1.1.2).</summary>
    private void InitializeNewAttempt(ScormActivityDefinition activity, ActivityState state)
    {
        state.AttemptProgressStatus = false;
        state.AttemptCompletionStatus = false;
        state.AttemptCompletionAmountStatus = false;
        state.AttemptCompletionAmount = 0;
        state.AttemptExperiencedDurationSecond = 0;
        foreach (var objective in activity.Sequencing.ObjectiveList)
            ResetLocalObjective(Local(activity, objective));

        if (activity.IsLeaf) return;

        ApplySelectionAndRandomization(activity, state, isNewAttempt: true);

        // Lượt thử thứ hai trở đi trên một cụm: thông tin của các con từ lượt CŨ không còn được tính
        // (useCurrentAttemptObjectiveInfo / useCurrentAttemptProgressInfo, mặc định đều bật).
        if (state.AttemptCount <= 1) return;
        foreach (var descendant in Descendants(activity))
        {
            var descendantState = S(descendant);
            if (descendantState.IsActive || descendantState.IsSuspended) continue;
            if (activity.Sequencing.UseCurrentAttemptProgressInfo)
            {
                descendantState.AttemptProgressStatus = false;
                descendantState.AttemptCompletionStatus = false;
                descendantState.AttemptCompletionAmountStatus = false;
            }
            if (activity.Sequencing.UseCurrentAttemptObjectiveInfo)
                foreach (var objective in descendant.Sequencing.ObjectiveList)
                {
                    var local = Local(descendant, objective);
                    local.ProgressStatus = false;
                    local.MeasureStatus = false;
                }
        }
    }

    private static void ResetLocalObjective(ObjectiveState local)
    {
        local.ProgressStatus = false;
        local.SatisfiedStatus = false;
        local.MeasureStatus = false;
        local.NormalizedMeasure = 0;
        local.RawScore = local.MinScore = local.MaxScore = local.ProgressMeasure = null;
        local.CompletionStatus = null;
    }

    /// <summary>SR.1 Select Children + SR.2 Randomize Children.</summary>
    private void ApplySelectionAndRandomization(ScormActivityDefinition activity, ActivityState state, bool isNewAttempt)
    {
        if (activity.IsLeaf || state.IsActive || state.IsSuspended) return;
        var sequencing = activity.Sequencing;

        bool Due(string timing) => timing switch
        {
            "once" => !isNewAttempt && !state.ProgressStatus,
            "onEachNewAttempt" => true,
            _ => false,
        };

        var childIdList = state.AvailableChildIdList ?? activity.Children.Select(x => x.Id).ToList();
        var changed = false;

        if (Due(sequencing.SelectionTiming) && sequencing.SelectCount is int count && count > 0 && count < activity.Children.Count)
        {
            var picked = activity.Children.Select((child, i) => (child.Id, i))
                .OrderBy(_ => Random.Shared.Next()).Take(count)
                .OrderBy(x => x.i).Select(x => x.Id).ToList();
            childIdList = picked;
            changed = true;
        }

        if (Due(sequencing.RandomizationTiming) && sequencing.ReorderChildren)
        {
            childIdList = childIdList.OrderBy(_ => Random.Shared.Next()).ToList();
            changed = true;
        }

        if (changed) state.AvailableChildIdList = childIdList;
    }

    /// <summary>DB.2.1 Clear Suspended Activity Subprocess.</summary>
    private void ClearSuspendedActivitySubprocess(ScormActivityDefinition activity)
    {
        var suspended = Suspended;
        if (suspended is null) return;

        var common = CommonAncestor(activity, suspended);
        foreach (var item in PathUpExclusive(suspended, common).Append(common))
        {
            if (item.IsLeaf) S(item).IsSuspended = false;
            else if (!Available(item).Any(child => S(child).IsSuspended)) S(item).IsSuspended = false;
        }
        Suspended = null;
    }

    // ==================== UP.x TIỆN ÍCH ====================

    /// <summary>UP.1 Limit Conditions Check Process: attemptLimit (bắt buộc theo chuẩn) và beginTimeLimit / endTimeLimit
    /// (tùy chọn — hệ thống dùng làm lịch mở / hạn chót của từng mục). Giới hạn thời lượng không được áp.</summary>
    private bool LimitConditionsCheckProcess(ScormActivityDefinition activity)
    {
        if (!activity.Sequencing.Tracked) return false;
        var state = S(activity);
        if (state.IsActive || state.IsSuspended) return false;
        if (activity.Sequencing.AttemptLimit is int limit && state.ProgressStatus && state.AttemptCount >= limit) return true;

        var now = Clock();
        if (activity.Sequencing.BeginTimeLimit is DateTime begin && now < begin) return true;
        if (activity.Sequencing.EndTimeLimit is DateTime end && now > end) return true;
        return false;
    }

    /// <summary>Đồng hồ cho giới hạn thời gian — gán được để kiểm thử.</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

    /// <summary>UP.2 Sequencing Rules Check Process — trả về hành động của luật đầu tiên thỏa, null nếu không có.</summary>
    private string? SequencingRulesCheck(ScormActivityDefinition activity, List<SequencingRuleDefinition> ruleList, params string[] actionList)
    {
        foreach (var rule in ruleList)
        {
            if (!actionList.Contains(rule.Action)) continue;
            if (SequencingRuleCheckSubprocess(activity, rule) == true) return rule.Action;
        }
        return null;
    }

    /// <summary>UP.2.1 — logic ba trị: true / false / null (chưa biết). Luật chỉ "bắn" khi kết quả là true.</summary>
    private bool? SequencingRuleCheckSubprocess(ScormActivityDefinition activity, SequencingRuleDefinition rule)
    {
        var valueList = rule.ConditionList.Select(condition =>
        {
            var value = EvaluateCondition(activity, condition.Condition, condition.ReferencedObjective, condition.MeasureThreshold);
            return condition.Operator == "not" ? Not(value) : value;
        }).ToList();
        return Combine(valueList, rule.ConditionCombination);
    }

    private bool? EvaluateCondition(ScormActivityDefinition activity, string condition, string? referencedObjective, double threshold)
    {
        var objective = string.IsNullOrEmpty(referencedObjective)
            ? PrimaryOf(activity)
            : activity.Sequencing.ObjectiveList.FirstOrDefault(x => x.Id == referencedObjective);
        var state = S(activity);

        switch (condition)
        {
            case "satisfied":
                return objective is null ? null : ObjectiveSatisfied(activity, objective);
            case "objectiveStatusKnown":
                return objective is not null && ObjectiveSatisfied(activity, objective).HasValue;
            case "objectiveMeasureKnown":
                return objective is not null && ObjectiveMeasure(activity, objective).HasValue;
            case "objectiveMeasureGreaterThan":
            {
                var measure = objective is null ? null : ObjectiveMeasure(activity, objective);
                return measure.HasValue ? measure.Value > threshold : null;
            }
            case "objectiveMeasureLessThan":
            {
                var measure = objective is null ? null : ObjectiveMeasure(activity, objective);
                return measure.HasValue ? measure.Value < threshold : null;
            }
            case "completed":
            {
                if (!string.IsNullOrEmpty(referencedObjective) && objective is null) return null;
                var (known, completed) = string.IsNullOrEmpty(referencedObjective)
                    ? ActivityCompletion(activity) : ObjectiveCompletion(activity, objective!);
                return known ? completed : null;
            }
            case "activityProgressKnown":
                if (!string.IsNullOrEmpty(referencedObjective))
                    return objective is not null && ObjectiveCompletion(activity, objective).Known;
                return state.ProgressStatus && ActivityCompletion(activity).Known;
            case "attempted":
                return state.ProgressStatus && state.AttemptCount > 0;
            case "attemptLimitExceeded":
                return LimitConditionsCheckProcess(activity);
            case "timeLimitExceeded":
            case "outsideAvailableTimeRange":
                return false;
            case "always":
                return true;
            default:
                return null;
        }
    }

    private static bool? Not(bool? value) => value.HasValue ? !value.Value : null;

    private static bool? Combine(List<bool?> valueList, string combination)
    {
        if (valueList.Count == 0) return null;
        if (combination == "any")
        {
            if (valueList.Any(v => v == true)) return true;
            return valueList.Any(v => v is null) ? null : false;
        }
        if (valueList.Any(v => v == false)) return false;
        return valueList.Any(v => v is null) ? null : true;
    }

    /// <summary>UP.3 Terminate Descendent Attempts Process.</summary>
    private void TerminateDescendentAttemptsProcess(ScormActivityDefinition activity)
    {
        var current = Current;
        if (current is null) return;

        var common = CommonAncestor(current, activity);
        foreach (var item in PathUpExclusive(current, common).Skip(1))
            EndAttemptProcess(item);
    }

    /// <summary>UP.4 End Attempt Process.</summary>
    private void EndAttemptProcess(ScormActivityDefinition activity)
    {
        var state = S(activity);
        if (activity.IsLeaf)
        {
            if (activity.Sequencing.Tracked && !state.IsSuspended)
            {
                // Nội dung không tự báo hoàn thành / đạt thì LMS coi là hoàn thành và đạt khi thoát.
                if (!activity.Sequencing.CompletionSetByContent && !state.AttemptProgressStatus)
                {
                    state.AttemptProgressStatus = true;
                    state.AttemptCompletionStatus = true;
                }
                if (!activity.Sequencing.ObjectiveSetByContent)
                {
                    var primary = PrimaryOf(activity);
                    if (!Local(activity, primary).ProgressStatus) SetObjectiveSatisfied(activity, primary, true);
                }
            }
        }
        else
        {
            state.IsSuspended = Available(activity).Any(child => S(child).IsSuspended);
        }

        state.IsActive = false;
        OverallRollupProcess(activity);
    }

    /// <summary>UP.5 Check Activity Process — true nghĩa là hoạt động đang bị CHẶN (disabled hoặc hết lượt).</summary>
    private bool CheckActivityProcess(ScormActivityDefinition activity)
        => SequencingRulesCheck(activity, activity.Sequencing.PreConditionRuleList, "disabled") is not null
           || LimitConditionsCheckProcess(activity);

    // ==================== RB.1.x ROLLUP ====================

    /// <summary>RB.1.5 Overall Rollup Process.</summary>
    public void OverallRollupProcess(ScormActivityDefinition activity)
    {
        foreach (var item in PathToRoot(activity))
        {
            if (!item.IsLeaf)
            {
                MeasureRollupProcess(item);
                CompletionMeasureRollupProcess(item);
            }
            ObjectiveRollupProcess(item);
            ActivityProgressRollupProcess(item);
            WriteActivityCompletionMaps(item);
            RecordBestResult(item);
        }
        AfterRollup?.Invoke(this);
    }

    /// <summary>Ghi nhận kết quả tốt nhất từng đạt (phần của LMS, xem ActivityState.EverCompleted).</summary>
    private void RecordBestResult(ScormActivityDefinition activity)
    {
        var state = S(activity);
        var (known, completed) = ActivityCompletion(activity);
        if (known && completed) state.EverCompleted = true;
        var satisfied = ObjectiveSatisfied(activity, PrimaryOf(activity));
        if (satisfied == true) state.EverSatisfied = true;
        if (satisfied == false) state.EverFailed = true;
        if (ObjectiveMeasure(activity, PrimaryOf(activity)) is double measure && (state.BestMeasure is null || measure > state.BestMeasure))
            state.BestMeasure = measure;
    }

    /// <summary>
    /// Đặt trạng thái của một hoạt động lá theo dữ liệu CÓ THẨM QUYỀN của máy chủ, rồi rollup.
    ///
    /// Dùng cho SCO do chính hệ thống phục vụ (bài học, bài kiểm tra chấm trên máy chủ, mô phỏng 3D): kết quả thật
    /// nằm ở bảng nghiệp vụ (LessonProgress, QuizAttempt, ScenarioAttempt), SCO chỉ là người đưa tin — học viên mở
    /// devtools gọi SetValue("cmi.success_status","passed") không được vượt qua bài kiểm tra. Cũng là đường chuyển
    /// tiến độ học từ trước khi có SCORM sang mô hình theo dõi của chuẩn.
    ///
    /// Mọi giá trị đều GHI ĐÈ, kể cả "chưa biết" (null): đó là đường thu hồi một trạng thái nội dung tự báo mà máy
    /// chủ không công nhận. Trả về true nếu có gì thay đổi.
    /// </summary>
    public bool ApplyAuthoritativeStatus(string activityId, ScormAuthoritativeStatus status)
    {
        if (Find(activityId) is not ScormActivityDefinition activity || !activity.IsLeaf || !activity.Sequencing.Tracked) return false;
        var state = S(activity);
        var primary = PrimaryOf(activity);
        var before = (state.AttemptCount, state.AttemptProgressStatus, state.AttemptCompletionStatus,
            ObjectiveSatisfied(activity, primary), ObjectiveMeasure(activity, primary));

        if (status.Attempted && state.AttemptCount == 0)
        {
            // Học viên đã học mục này trước khi có SCORM (hoặc ở màn hình cũ): coi như đã có một lượt thử.
            state.AttemptCount = 1;
            state.ProgressStatus = true;
        }
        state.AttemptProgressStatus = status.Completed.HasValue;
        state.AttemptCompletionStatus = status.Completed ?? false;

        SetObjectiveSatisfied(activity, primary, status.Satisfied);
        SetObjectiveMeasure(activity, primary, status.Measure);
        // Giá trị "chưa biết" không tự lan ra mục tiêu toàn cục (xem SetObjectiveSatisfied) — ở đây phải xóa tay,
        // không thì trạng thái giả đã kịp ghi ra ngoài vẫn còn đó cho các hoạt động khác đọc.
        foreach (var map in primary.MapList)
        {
            if (!State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global)) continue;
            if (map.WriteSatisfiedStatus && !status.Satisfied.HasValue) global.ProgressStatus = false;
            if (map.WriteNormalizedMeasure && !status.Measure.HasValue) global.MeasureStatus = false;
        }

        var after = (state.AttemptCount, state.AttemptProgressStatus, state.AttemptCompletionStatus,
            ObjectiveSatisfied(activity, primary), ObjectiveMeasure(activity, primary));
        if (before == after) return false;
        OverallRollupProcess(activity);
        return true;
    }

    /// <summary>Tóm tắt từng hoạt động cho lộ trình / báo cáo: hoàn thành, đạt, điểm, số lượt — gồm cả kết quả tốt
    /// nhất từng đạt.</summary>
    public Dictionary<string, ScormActivitySummary> ActivitySummaryMap()
    {
        var map = new Dictionary<string, ScormActivitySummary>(StringComparer.Ordinal);
        foreach (var activity in _index.Order)
        {
            var state = S(activity);
            var (known, completed) = ActivityCompletion(activity);
            map[activity.Id] = new ScormActivitySummary(
                known ? completed : null,
                ObjectiveSatisfied(activity, PrimaryOf(activity)),
                ObjectiveMeasure(activity, PrimaryOf(activity)),
                state.AttemptCount, state.EverCompleted, state.EverSatisfied, state.EverFailed, state.BestMeasure);
        }
        return map;
    }

    /// <summary>RB.1.1a Measure Rollup Process.</summary>
    private void MeasureRollupProcess(ScormActivityDefinition activity)
    {
        double total = 0, counted = 0;
        var valid = false;
        foreach (var child in Available(activity))
        {
            if (!child.Sequencing.Tracked) continue;
            counted += child.Sequencing.ObjectiveMeasureWeight;
            var measure = ObjectiveMeasure(child, PrimaryOf(child));
            if (measure.HasValue)
            {
                total += measure.Value * child.Sequencing.ObjectiveMeasureWeight;
                valid = true;
            }
        }
        SetObjectiveMeasure(activity, PrimaryOf(activity), valid && counted > 0 ? total / counted : null);
    }

    /// <summary>RB.1.1b Completion Measure Rollup Process (4th Ed).</summary>
    private void CompletionMeasureRollupProcess(ScormActivityDefinition activity)
    {
        double total = 0, counted = 0;
        var valid = false;
        foreach (var child in Available(activity))
        {
            if (!child.Sequencing.Tracked) continue;
            var weight = child.CompletionThreshold?.ProgressWeight ?? 1.0;
            counted += weight;
            var childState = S(child);
            if (childState.AttemptCompletionAmountStatus)
            {
                total += childState.AttemptCompletionAmount * weight;
                valid = true;
            }
        }
        var state = S(activity);
        state.AttemptCompletionAmountStatus = valid && counted > 0;
        state.AttemptCompletionAmount = state.AttemptCompletionAmountStatus ? total / counted : 0;
    }

    /// <summary>RB.1.2 Objective Rollup Process.</summary>
    private void ObjectiveRollupProcess(ScormActivityDefinition activity)
    {
        var primary = PrimaryOf(activity);
        if (primary.SatisfiedByMeasure)
        {
            ObjectiveRollupUsingMeasureProcess(activity, primary);
            return;
        }
        if (activity.IsLeaf) return;

        // RB.1.2b: luật "không đạt" xét trước, "đạt" xét sau — cùng thỏa thì "đạt" thắng.
        if (RollupRuleCheckSubprocess(activity, "notSatisfied")) SetObjectiveSatisfied(activity, primary, false);
        if (RollupRuleCheckSubprocess(activity, "satisfied")) SetObjectiveSatisfied(activity, primary, true);
    }

    /// <summary>RB.1.2a Objective Rollup Using Measure Process.</summary>
    private void ObjectiveRollupUsingMeasureProcess(ScormActivityDefinition activity, ObjectiveDefinition primary)
    {
        var measure = ObjectiveMeasure(activity, primary);
        if (!measure.HasValue)
        {
            Local(activity, primary).ProgressStatus = false;
            return;
        }
        if (!S(activity).IsActive || activity.Sequencing.MeasureSatisfactionIfActive)
            SetObjectiveSatisfied(activity, primary, measure.Value >= primary.MinNormalizedMeasure);
        else
            Local(activity, primary).ProgressStatus = false;
    }

    /// <summary>RB.1.3 Activity Progress Rollup Process.</summary>
    private void ActivityProgressRollupProcess(ScormActivityDefinition activity)
    {
        var state = S(activity);
        if (activity.CompletionThreshold is { CompletedByMeasure: true } threshold)
        {
            // RB.1.3a Using Measure.
            if (!state.AttemptCompletionAmountStatus)
            {
                state.AttemptProgressStatus = false;
                return;
            }
            state.AttemptProgressStatus = true;
            state.AttemptCompletionStatus = state.AttemptCompletionAmount >= threshold.MinProgressMeasure;
            return;
        }
        if (activity.IsLeaf) return;

        // RB.1.3b Using Rules.
        if (RollupRuleCheckSubprocess(activity, "incomplete"))
        {
            state.AttemptProgressStatus = true;
            state.AttemptCompletionStatus = false;
        }
        if (RollupRuleCheckSubprocess(activity, "completed"))
        {
            state.AttemptProgressStatus = true;
            state.AttemptCompletionStatus = true;
        }
    }

    /// <summary>RB.1.4 Rollup Rule Check Subprocess.</summary>
    private bool RollupRuleCheckSubprocess(ScormActivityDefinition activity, string action)
    {
        foreach (var rule in _index.RollupRuleOf[activity])
        {
            if (rule.Action != action) continue;

            var bag = new List<bool?>();
            foreach (var child in Available(activity))
            {
                if (!child.Sequencing.Tracked) continue;
                if (!CheckChildForRollupSubprocess(child, action)) continue;

                var valueList = rule.ConditionList.Select(condition =>
                {
                    var value = EvaluateCondition(child, condition.Condition, null, 0);
                    return condition.Operator == "not" ? Not(value) : value;
                }).ToList();
                bag.Add(Combine(valueList, rule.ConditionCombination));
            }

            // Không có con nào đóng góp thì luật không bắn (lỗi kinh điển của bản 3rd: "all" trên tập rỗng = true).
            if (bag.Count == 0) continue;

            var status = rule.ChildActivitySet switch
            {
                "all" => bag.All(v => v == true),
                "any" => bag.Any(v => v == true),
                "none" => bag.All(v => v == false),
                "atLeastCount" => bag.Count(v => v == true) >= rule.MinimumCount,
                "atLeastPercent" => (double)bag.Count(v => v == true) / bag.Count >= rule.MinimumPercent,
                _ => false,
            };
            if (status) return true;
        }
        return false;
    }

    /// <summary>RB.1.4.2 Check Child for Rollup Subprocess.</summary>
    private bool CheckChildForRollupSubprocess(ScormActivityDefinition child, string action)
    {
        var sequencing = child.Sequencing;
        var state = S(child);

        string required;
        if (action is "satisfied" or "notSatisfied")
        {
            if (!sequencing.RollupObjectiveSatisfied) return false;
            required = action == "satisfied" ? sequencing.RequiredForSatisfied : sequencing.RequiredForNotSatisfied;
        }
        else if (action is "completed" or "incomplete")
        {
            if (!sequencing.RollupProgressCompletion) return false;
            required = action == "completed" ? sequencing.RequiredForCompleted : sequencing.RequiredForIncomplete;
        }
        else return false;

        return required switch
        {
            "ifNotSuspended" => !(!state.ProgressStatus || (state.AttemptCount > 0 && state.IsSuspended)),
            "ifAttempted" => state.ProgressStatus && state.AttemptCount > 0,
            "ifNotSkipped" => SequencingRulesCheck(child, sequencing.PreConditionRuleList, "skip") is null,
            _ => true,
        };
    }

    // ==================== RTE -> TRACKING ====================

    /// <summary>Chép dữ liệu run-time SCO vừa báo vào mô hình theo dõi của hoạt động lá rồi rollup lên gốc (SCORM
    /// 2004 RTE 4.2 ↔ SN 4.2: completion_status → Attempt Completion, success_status / score.scaled → mục tiêu
    /// chính, cmi.objectives → các mục tiêu cùng ID).</summary>
    public void ApplyRuntimeReport(string activityId, ScormRuntimeReport report)
    {
        var activity = Find(activityId);
        if (activity is null || !activity.IsLeaf) return;
        var state = S(activity);

        if (report.SharedDataWriteById is not null)
            foreach (var (targetId, value) in report.SharedDataWriteById)
                if (activity.SharedDataList.Any(m => m.TargetId == targetId && m.WriteSharedData))
                    State.SharedDataById[targetId] = value;

        if (!activity.Sequencing.Tracked) return;

        switch (report.CompletionStatus)
        {
            case "completed":
                state.AttemptProgressStatus = true;
                state.AttemptCompletionStatus = true;
                break;
            case "incomplete":
                state.AttemptProgressStatus = true;
                state.AttemptCompletionStatus = false;
                break;
            default:
                // "not attempted" / "unknown": chưa biết — để End Attempt áp mặc định nếu nội dung không tự đặt.
                state.AttemptProgressStatus = false;
                break;
        }

        state.AttemptCompletionAmountStatus = report.ProgressMeasure.HasValue;
        state.AttemptCompletionAmount = report.ProgressMeasure ?? 0;

        var primary = PrimaryOf(activity);
        SetObjectiveSatisfied(activity, primary, report.SuccessStatus switch
        {
            "passed" => true,
            "failed" => false,
            _ => null,
        });
        SetObjectiveMeasure(activity, primary, report.ScoreScaled);
        SetObjectiveExtended(activity, primary, report.ScoreRaw, report.ScoreMin, report.ScoreMax,
            report.CompletionStatus, report.ProgressMeasure);

        foreach (var reported in report.ObjectiveList)
        {
            if (string.IsNullOrEmpty(reported.Id) || reported.Id == primary.Id) continue;
            var objective = activity.Sequencing.ObjectiveList.FirstOrDefault(x => !x.IsPrimary && x.Id == reported.Id);
            if (objective is null) continue;

            SetObjectiveSatisfied(activity, objective, reported.SuccessStatus switch
            {
                "passed" => true,
                "failed" => false,
                _ => null,
            });
            SetObjectiveMeasure(activity, objective, reported.ScoreScaled);
            SetObjectiveExtended(activity, objective, reported.ScoreRaw, reported.ScoreMin, reported.ScoreMax,
                reported.CompletionStatus, reported.ProgressMeasure);
        }

        state.AttemptExperiencedDurationSecond += Math.Max(0, report.SessionTimeSecond);
        state.ActivityExperiencedDurationSecond += Math.Max(0, report.SessionTimeSecond);

        OverallRollupProcess(activity);
    }

    /// <summary>SCO kết thúc với cmi.exit = "suspend": lượt thử của nó được giữ lại để học tiếp.</summary>
    public void MarkSuspendedByContent(string activityId)
    {
        if (Find(activityId) is ScormActivityDefinition activity && activity.Sequencing.Tracked)
            S(activity).IsSuspended = true;
    }

    // ==================== ĐỌC TRẠNG THÁI CHO TRÌNH PHÁT ====================

    public ScormActivityDefinition? FindActivity(string? id) => Find(id);

    public bool HasCurrentActivity => Current is not null;

    public bool IsCurrentActivityActive => Current is ScormActivityDefinition current && S(current).IsActive;

    public bool HasSuspendedActivity => Suspended is not null;

    /// <summary>Số lượt thử đã bắt đầu trên hoạt động gốc.</summary>
    public int RootAttemptCount => S(Root).AttemptCount;

    /// <summary>Tóm tắt hoạt động gốc: hoàn thành, đạt, điểm.</summary>
    public (string Completion, string Success, double? ScoreScaled) RootSummary()
    {
        var state = S(Root);
        var satisfied = ObjectiveSatisfied(Root, PrimaryOf(Root));
        return (
            state.AttemptProgressStatus ? (state.AttemptCompletionStatus ? "completed" : "incomplete") : (state.ProgressStatus ? "incomplete" : "unknown"),
            satisfied switch { true => "passed", false => "failed", _ => "unknown" },
            ObjectiveMeasure(Root, PrimaryOf(Root)));
    }

    /// <summary>Giá trị khởi tạo cmi.objectives của một SCO: mọi mục tiêu có ID của hoạt động, trạng thái đọc qua ánh
    /// xạ toàn cục (RTE 4.2.17 — LMS phải khởi tạo sẵn các mục tiêu khai trong sequencing).</summary>
    public List<ScormObjectiveReport> InitialObjectiveList(string activityId)
    {
        var list = new List<ScormObjectiveReport>();
        if (Find(activityId) is not ScormActivityDefinition activity) return list;

        foreach (var objective in activity.Sequencing.ObjectiveList.Where(x => !string.IsNullOrEmpty(x.Id)))
        {
            var local = Local(activity, objective);
            var item = new ScormObjectiveReport
            {
                Id = objective.Id!,
                SuccessStatus = ObjectiveSatisfied(activity, objective) switch { true => "passed", false => "failed", _ => "unknown" },
                ScoreScaled = ObjectiveMeasure(activity, objective),
                ScoreRaw = local.RawScore,
                ScoreMin = local.MinScore,
                ScoreMax = local.MaxScore,
                CompletionStatus = local.CompletionStatus ?? "unknown",
                ProgressMeasure = local.ProgressMeasure,
            };

            foreach (var map in objective.MapList)
            {
                if (!State.GlobalObjectiveById.TryGetValue(map.TargetObjectiveId, out var global)) continue;
                if (map.ReadRawScore && global.RawScore.HasValue) item.ScoreRaw = global.RawScore;
                if (map.ReadMinScore && global.MinScore.HasValue) item.ScoreMin = global.MinScore;
                if (map.ReadMaxScore && global.MaxScore.HasValue) item.ScoreMax = global.MaxScore;
                if (map.ReadCompletionStatus && global.CompletionStatus is not null) item.CompletionStatus = global.CompletionStatus;
                if (map.ReadProgressMeasure && global.ProgressMeasure.HasValue) item.ProgressMeasure = global.ProgressMeasure;
            }
            list.Add(item);
        }
        return list;
    }

    /// <summary>adl.data của một SCO: các kho nó được đọc, kèm nội dung hiện có.</summary>
    public List<(string Id, string Store, bool Readable, bool Writable)> SharedDataFor(string activityId)
    {
        var list = new List<(string, string, bool, bool)>();
        if (Find(activityId) is not ScormActivityDefinition activity) return list;
        foreach (var map in activity.SharedDataList)
        {
            State.SharedDataById.TryGetValue(map.TargetId, out var store);
            list.Add((map.TargetId, store ?? "", map.ReadSharedData, map.WriteSharedData));
        }
        return list;
    }

    /// <summary>Thử trước (trên BẢN SAO trạng thái) xem từng yêu cầu điều hướng có đưa tới một hoạt động giao được
    /// không — để bật/tắt nút Tiếp / Trước, đánh dấu mục nào trong mục lục bấm được, và trả lời
    /// adl.nav.request_valid.* cho SCO.</summary>
    public ScormNavigationAvailability EvaluateAvailability()
    {
        var snapshot = JsonSerializer.Serialize(State);

        bool Try(ScormNavigationRequest request)
        {
            var copy = JsonSerializer.Deserialize<ScormSequencingState>(snapshot)!;
            var result = new ScormSequencer(_tree, copy, _index) { AfterRollup = AfterRollup, Clock = Clock }.Navigate(request);
            return result.IsValid && (result.DeliveredActivityId is not null || result.EndSequencingSession);
        }

        var choiceSet = new HashSet<string>(StringComparer.Ordinal);
        var jumpSet = new HashSet<string>(StringComparer.Ordinal);

        // Cây quá lớn thì bỏ phần thử từng mục (mỗi lần thử là một bản sao trạng thái) — vẫn trả lời được bằng
        // phép kiểm rẻ hơn của NB.2.1 + điều kiện chặn của chính hoạt động.
        var simulateEach = _index.Order.Count <= 300;
        foreach (var activity in _index.Order)
        {
            if (simulateEach)
            {
                if (Try(new ScormNavigationRequest("choice", activity.Id))) choiceSet.Add(activity.Id);
                if (Current is not null && Try(new ScormNavigationRequest("jump", activity.Id))) jumpSet.Add(activity.Id);
            }
            else if (NavigationRequestProcess(new ScormNavigationRequest("choice", activity.Id)).Valid && !CheckActivityProcess(activity))
            {
                choiceSet.Add(activity.Id);
                jumpSet.Add(activity.Id);
            }
        }

        return new ScormNavigationAvailability
        {
            CanContinue = Current is not null && Try(new ScormNavigationRequest("continue")),
            CanPrevious = Current is not null && Try(new ScormNavigationRequest("previous")),
            ChoiceValidIdSet = choiceSet,
            JumpValidIdSet = jumpSet,
        };
    }

    /// <summary>Tình trạng từng hoạt động, theo thứ tự tiền thứ tự — để vẽ mục lục.</summary>
    public List<ScormActivityStatus> ActivityStatusList(ScormNavigationAvailability availability)
    {
        // Chỉ những hoạt động nằm trong Available Children (sau chọn lọc ngẫu nhiên — selectCount) mới có mặt trong
        // lượt học này; mục bị loại ra không được hiện trong mục lục.
        // Duyệt theo Available Children nên thứ tự cũng là thứ tự đã xáo (randomization) của lượt này.
        var orderedList = new List<ScormActivityDefinition>();
        void Collect(ScormActivityDefinition activity)
        {
            orderedList.Add(activity);
            foreach (var child in Available(activity)) Collect(child);
        }
        Collect(Root);

        var list = new List<ScormActivityStatus>(orderedList.Count);
        foreach (var activity in orderedList)
        {
            var state = S(activity);
            var satisfied = ObjectiveSatisfied(activity, PrimaryOf(activity));
            var hidden = SequencingRulesCheck(activity, activity.Sequencing.PreConditionRuleList, "hiddenFromChoice") is not null;
            var (completionKnown, completed) = ActivityCompletion(activity);
            list.Add(new ScormActivityStatus(
                activity.Id, activity.Title, _index.DepthOf[activity], activity.IsLeaf, activity.IsVisible,
                completionKnown ? (completed ? "completed" : "incomplete")
                    : (state.ProgressStatus && state.AttemptCount > 0 ? "incomplete" : "unknown"),
                satisfied switch { true => "passed", false => "failed", _ => "unknown" },
                ObjectiveMeasure(activity, PrimaryOf(activity)),
                state.AttemptCount, state.IsActive, state.IsSuspended, Current == activity,
                availability.ChoiceValidIdSet.Contains(activity.Id), hidden, CheckActivityProcess(activity)));
        }
        return list;
    }
}
