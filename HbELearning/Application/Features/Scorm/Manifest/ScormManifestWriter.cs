// File: Application/Features/Scorm/Manifest/ScormManifestWriter.cs
using System.Globalization;
using System.Xml.Linq;
using HbELearning.Application.Features.Scorm.Model;

namespace HbELearning.Application.Features.Scorm.Manifest;

/// <summary>Tài nguyên của một hoạt động lá khi ghi manifest.</summary>
/// <param name="Href">Địa chỉ nội dung; phần "?..." / "#..." được tách sang thuộc tính parameters của item.</param>
/// <param name="FileList">Các tệp liệt kê trong &lt;resource&gt; (không cần lặp lại Href).</param>
/// <param name="DependencyId">identifier của tài nguyên dùng chung (asset) mà SCO này phụ thuộc, nếu có.</param>
public sealed record ScormResourceSpec(string Href, bool IsSco, IReadOnlyList<string> FileList, string? DependencyId = null);

/// <summary>
/// Ghi một <see cref="ScormActivityTree"/> ra imsmanifest.xml theo SCORM 2004 4th Edition (IMS CP 1.1.4 + ADL CP,
/// IMS SS 1.0, ADL SEQ / NAV). Ngược chiều với <see cref="ScormManifestParser"/>: chỉ ghi những giá trị KHÁC mặc
/// định của chuẩn, để manifest đọc được bằng mắt và LMS nào đọc cũng ra đúng một cây.
///
/// Thứ tự thẻ con theo đúng schema (LMS kiểm schema chặt sẽ từ chối gói sai thứ tự).
/// </summary>
public static class ScormManifestWriter
{
    public static readonly XNamespace Cp = "http://www.imsglobal.org/xsd/imscp_v1p1";
    public static readonly XNamespace Adlcp = "http://www.adlnet.org/xsd/adlcp_v1p3";
    public static readonly XNamespace Adlseq = "http://www.adlnet.org/xsd/adlseq_v1p3";
    public static readonly XNamespace Adlnav = "http://www.adlnet.org/xsd/adlnav_v1p3";
    public static readonly XNamespace Imsss = "http://www.imsglobal.org/xsd/imsss";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <param name="resourceOf">Tài nguyên của từng lá; trả null thì lá đó không có identifierref (không nên xảy ra).</param>
    /// <param name="extraResourceList">Tài nguyên dùng chung (thư viện JS / CSS) — ghi thêm vào &lt;resources&gt;.</param>
    public static XDocument Write(ScormActivityTree tree, Func<ScormActivityDefinition, ScormResourceSpec?> resourceOf,
        IEnumerable<XElement>? extraResourceList = null)
    {
        var resources = new XElement(Cp + "resources");
        if (extraResourceList is not null) foreach (var extra in extraResourceList) resources.Add(extra);

        var organization = new XElement(Cp + "organization",
            new XAttribute("identifier", SafeId(tree.Root.Id)),
            new XAttribute(Adlseq + "objectivesGlobalToSystem", tree.ObjectivesGlobalToSystem ? "true" : "false"),
            new XAttribute(Adlcp + "sharedDataGlobalToSystem", tree.SharedDataGlobalToSystem ? "true" : "false"),
            new XElement(Cp + "title", tree.Root.Title));
        foreach (var child in tree.Root.Children) organization.Add(WriteItem(child, resourceOf, resources));
        if (WriteSequencing(tree.Root) is XElement rootSequencing) organization.Add(rootSequencing);

        var manifest = new XElement(Cp + "manifest",
            new XAttribute("identifier", SafeId(tree.ManifestIdentifier ?? "MANIFEST")),
            new XAttribute("version", "1"),
            new XAttribute(XNamespace.Xmlns + "adlcp", Adlcp),
            new XAttribute(XNamespace.Xmlns + "adlseq", Adlseq),
            new XAttribute(XNamespace.Xmlns + "adlnav", Adlnav),
            new XAttribute(XNamespace.Xmlns + "imsss", Imsss),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation",
                "http://www.imsglobal.org/xsd/imscp_v1p1 imscp_v1p1.xsd "
                + "http://www.adlnet.org/xsd/adlcp_v1p3 adlcp_v1p3.xsd "
                + "http://www.adlnet.org/xsd/adlseq_v1p3 adlseq_v1p3.xsd "
                + "http://www.adlnet.org/xsd/adlnav_v1p3 adlnav_v1p3.xsd "
                + "http://www.imsglobal.org/xsd/imsss imsss_v1p0.xsd"),
            new XElement(Cp + "metadata",
                new XElement(Cp + "schema", "ADL SCORM"),
                new XElement(Cp + "schemaversion", "2004 4th Edition")),
            new XElement(Cp + "organizations", new XAttribute("default", SafeId(tree.Root.Id)), organization),
            resources);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), manifest);
    }

    /// <summary>Chuỗi XML hoàn chỉnh (kèm dòng khai báo).</summary>
    public static string ToXmlString(XDocument document) => document.Declaration + Environment.NewLine + document.Root;

    // ==================== ITEM ====================

    private static XElement WriteItem(ScormActivityDefinition activity, Func<ScormActivityDefinition, ScormResourceSpec?> resourceOf, XElement resources)
    {
        var item = new XElement(Cp + "item", new XAttribute("identifier", SafeId(activity.Id)));
        if (!activity.IsVisible) item.Add(new XAttribute("isvisible", "false"));
        item.Add(new XElement(Cp + "title", activity.Title));

        if (activity.IsLeaf && resourceOf(activity) is ScormResourceSpec spec)
        {
            var resourceId = SafeId("RES-" + activity.Id);
            var (href, parameters) = SplitHref(spec.Href);
            item.Add(new XAttribute("identifierref", resourceId));
            if (!string.IsNullOrEmpty(parameters)) item.Add(new XAttribute("parameters", parameters));

            var resource = new XElement(Cp + "resource",
                new XAttribute("identifier", resourceId),
                new XAttribute("type", "webcontent"),
                new XAttribute(Adlcp + "scormType", spec.IsSco ? "sco" : "asset"),
                new XAttribute("href", href));
            foreach (var file in new[] { href }.Concat(spec.FileList).Where(f => !IsAbsolute(f)).Distinct(StringComparer.Ordinal))
                resource.Add(new XElement(Cp + "file", new XAttribute("href", file)));
            if (spec.DependencyId is string dependency) resource.Add(new XElement(Cp + "dependency", new XAttribute("identifierref", dependency)));
            resources.Add(resource);
        }

        foreach (var child in activity.Children) item.Add(WriteItem(child, resourceOf, resources));

        if (activity.TimeLimitAction is string action) item.Add(new XElement(Adlcp + "timeLimitAction", action));
        if (activity.DataFromLms is string data) item.Add(new XElement(Adlcp + "dataFromLMS", data));
        if (activity.CompletionThreshold is CompletionThresholdDefinition threshold)
        {
            var element = new XElement(Adlcp + "completionThreshold");
            if (threshold.CompletedByMeasure) element.Add(new XAttribute("completedByMeasure", "true"));
            if (threshold.MinProgressMeasure != 1.0) element.Add(new XAttribute("minProgressMeasure", Num(threshold.MinProgressMeasure)));
            if (threshold.ProgressWeight != 1.0) element.Add(new XAttribute("progressWeight", Num(threshold.ProgressWeight)));
            item.Add(element);
        }
        if (WriteSequencing(activity) is XElement sequencing) item.Add(sequencing);
        if (activity.HideLmsUi.Count > 0)
            item.Add(new XElement(Adlnav + "presentation", new XElement(Adlnav + "navigationInterface",
                activity.HideLmsUi.Select(x => new XElement(Adlnav + "hideLMSUI", x)))));
        if (activity.SharedDataList.Count > 0)
            item.Add(new XElement(Adlcp + "data", activity.SharedDataList.Select(map => new XElement(Adlcp + "map",
                new XAttribute("targetID", map.TargetId),
                new XAttribute("readSharedData", Bool(map.ReadSharedData)),
                new XAttribute("writeSharedData", Bool(map.WriteSharedData))))));
        return item;
    }

    // ==================== SEQUENCING ====================

    private static XElement? WriteSequencing(ScormActivityDefinition activity)
    {
        var s = activity.Sequencing;
        var defaults = new SequencingDefinition();
        var element = new XElement(Imsss + "sequencing");

        // controlMode
        var controlMode = new XElement(Imsss + "controlMode");
        if (s.ControlChoice != defaults.ControlChoice) controlMode.Add(new XAttribute("choice", Bool(s.ControlChoice)));
        if (s.ControlChoiceExit != defaults.ControlChoiceExit) controlMode.Add(new XAttribute("choiceExit", Bool(s.ControlChoiceExit)));
        if (s.ControlFlow != defaults.ControlFlow) controlMode.Add(new XAttribute("flow", Bool(s.ControlFlow)));
        if (s.ControlForwardOnly != defaults.ControlForwardOnly) controlMode.Add(new XAttribute("forwardOnly", Bool(s.ControlForwardOnly)));
        if (s.UseCurrentAttemptObjectiveInfo != defaults.UseCurrentAttemptObjectiveInfo) controlMode.Add(new XAttribute("useCurrentAttemptObjectiveInfo", Bool(s.UseCurrentAttemptObjectiveInfo)));
        if (s.UseCurrentAttemptProgressInfo != defaults.UseCurrentAttemptProgressInfo) controlMode.Add(new XAttribute("useCurrentAttemptProgressInfo", Bool(s.UseCurrentAttemptProgressInfo)));
        if (controlMode.HasAttributes) element.Add(controlMode);

        // sequencingRules
        if (s.PreConditionRuleList.Count + s.ExitConditionRuleList.Count + s.PostConditionRuleList.Count > 0)
        {
            var rules = new XElement(Imsss + "sequencingRules");
            foreach (var rule in s.PreConditionRuleList) rules.Add(WriteRule("preConditionRule", rule));
            foreach (var rule in s.ExitConditionRuleList) rules.Add(WriteRule("exitConditionRule", rule));
            foreach (var rule in s.PostConditionRuleList) rules.Add(WriteRule("postConditionRule", rule));
            element.Add(rules);
        }

        // limitConditions
        var limit = new XElement(Imsss + "limitConditions");
        if (s.AttemptLimit is int attemptLimit) limit.Add(new XAttribute("attemptLimit", attemptLimit));
        if (s.AttemptAbsoluteDurationLimit is string duration) limit.Add(new XAttribute("attemptAbsoluteDurationLimit", duration));
        if (s.BeginTimeLimit is DateTime begin) limit.Add(new XAttribute("beginTimeLimit", begin.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        if (s.EndTimeLimit is DateTime end) limit.Add(new XAttribute("endTimeLimit", end.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        if (limit.HasAttributes) element.Add(limit);

        // rollupRules
        var rollup = new XElement(Imsss + "rollupRules");
        if (s.RollupObjectiveSatisfied != defaults.RollupObjectiveSatisfied) rollup.Add(new XAttribute("rollupObjectiveSatisfied", Bool(s.RollupObjectiveSatisfied)));
        if (s.RollupProgressCompletion != defaults.RollupProgressCompletion) rollup.Add(new XAttribute("rollupProgressCompletion", Bool(s.RollupProgressCompletion)));
        if (s.ObjectiveMeasureWeight != defaults.ObjectiveMeasureWeight) rollup.Add(new XAttribute("objectiveMeasureWeight", Num(s.ObjectiveMeasureWeight)));
        foreach (var rule in s.RollupRuleList)
        {
            var conditions = new XElement(Imsss + "rollupConditions");
            if (rule.ConditionCombination != "any") conditions.Add(new XAttribute("conditionCombination", rule.ConditionCombination));
            foreach (var condition in rule.ConditionList)
            {
                var c = new XElement(Imsss + "rollupCondition");
                if (condition.Operator != "noOp") c.Add(new XAttribute("operator", condition.Operator));
                c.Add(new XAttribute("condition", condition.Condition));
                conditions.Add(c);
            }
            var ruleElement = new XElement(Imsss + "rollupRule");
            if (rule.ChildActivitySet != "all") ruleElement.Add(new XAttribute("childActivitySet", rule.ChildActivitySet));
            if (rule.MinimumCount != 0) ruleElement.Add(new XAttribute("minimumCount", rule.MinimumCount));
            if (rule.MinimumPercent != 0) ruleElement.Add(new XAttribute("minimumPercent", Num(rule.MinimumPercent)));
            ruleElement.Add(conditions, new XElement(Imsss + "rollupAction", new XAttribute("action", rule.Action)));
            rollup.Add(ruleElement);
        }
        if (rollup.HasAttributes || rollup.HasElements) element.Add(rollup);

        // objectives (imsss) + adlseq:objectives cho phần ánh xạ mở rộng của 4th Ed
        var objectiveList = s.ObjectiveList.Where(o => o.IsPrimary ? (o.Id is not null || o.SatisfiedByMeasure || o.MapList.Count > 0) : o.Id is not null).ToList();
        if (objectiveList.Count > 0 && objectiveList.Any(o => !o.IsPrimary) && !objectiveList.Any(o => o.IsPrimary))
            objectiveList.Insert(0, s.PrimaryObjective ?? new ObjectiveDefinition { IsPrimary = true });
        if (objectiveList.Count > 0)
        {
            var objectives = new XElement(Imsss + "objectives");
            foreach (var objective in objectiveList.OrderByDescending(o => o.IsPrimary))
            {
                var o = new XElement(Imsss + (objective.IsPrimary ? "primaryObjective" : "objective"));
                if (objective.Id is not null) o.Add(new XAttribute("objectiveID", objective.Id));
                if (objective.SatisfiedByMeasure) o.Add(new XAttribute("satisfiedByMeasure", "true"));
                if (objective.MinNormalizedMeasure != 1.0) o.Add(new XElement(Imsss + "minNormalizedMeasure", Num(objective.MinNormalizedMeasure)));
                foreach (var map in objective.MapList.Where(m => m.ReadSatisfiedStatus || m.ReadNormalizedMeasure || m.WriteSatisfiedStatus || m.WriteNormalizedMeasure))
                {
                    var mapInfo = new XElement(Imsss + "mapInfo", new XAttribute("targetObjectiveID", map.TargetObjectiveId));
                    if (!map.ReadSatisfiedStatus) mapInfo.Add(new XAttribute("readSatisfiedStatus", "false"));
                    if (!map.ReadNormalizedMeasure) mapInfo.Add(new XAttribute("readNormalizedMeasure", "false"));
                    if (map.WriteSatisfiedStatus) mapInfo.Add(new XAttribute("writeSatisfiedStatus", "true"));
                    if (map.WriteNormalizedMeasure) mapInfo.Add(new XAttribute("writeNormalizedMeasure", "true"));
                    o.Add(mapInfo);
                }
                objectives.Add(o);
            }
            element.Add(objectives);
        }

        // randomizationControls
        var randomization = new XElement(Imsss + "randomizationControls");
        if (s.RandomizationTiming != "never") randomization.Add(new XAttribute("randomizationTiming", s.RandomizationTiming));
        if (s.SelectCount is int selectCount) randomization.Add(new XAttribute("selectCount", selectCount));
        if (s.ReorderChildren) randomization.Add(new XAttribute("reorderChildren", "true"));
        if (s.SelectionTiming != "never") randomization.Add(new XAttribute("selectionTiming", s.SelectionTiming));
        if (randomization.HasAttributes) element.Add(randomization);

        // deliveryControls
        var delivery = new XElement(Imsss + "deliveryControls");
        if (!s.Tracked) delivery.Add(new XAttribute("tracked", "false"));
        if (s.CompletionSetByContent) delivery.Add(new XAttribute("completionSetByContent", "true"));
        if (s.ObjectiveSetByContent) delivery.Add(new XAttribute("objectiveSetByContent", "true"));
        if (delivery.HasAttributes) element.Add(delivery);

        // adlseq
        var constrained = new XElement(Adlseq + "constrainedChoiceConsiderations");
        if (s.PreventActivation) constrained.Add(new XAttribute("preventActivation", "true"));
        if (s.ConstrainChoice) constrained.Add(new XAttribute("constrainChoice", "true"));
        if (constrained.HasAttributes) element.Add(constrained);

        var considerations = new XElement(Adlseq + "rollupConsiderations");
        if (s.RequiredForSatisfied != "always") considerations.Add(new XAttribute("requiredForSatisfied", s.RequiredForSatisfied));
        if (s.RequiredForNotSatisfied != "always") considerations.Add(new XAttribute("requiredForNotSatisfied", s.RequiredForNotSatisfied));
        if (s.RequiredForCompleted != "always") considerations.Add(new XAttribute("requiredForCompleted", s.RequiredForCompleted));
        if (s.RequiredForIncomplete != "always") considerations.Add(new XAttribute("requiredForIncomplete", s.RequiredForIncomplete));
        if (!s.MeasureSatisfactionIfActive) considerations.Add(new XAttribute("measureSatisfactionIfActive", "false"));
        if (considerations.HasAttributes) element.Add(considerations);

        var extendedList = s.ObjectiveList.Where(o => o.Id is not null && o.MapList.Any(IsExtended)).ToList();
        if (extendedList.Count > 0)
            element.Add(new XElement(Adlseq + "objectives", extendedList.Select(o => new XElement(Adlseq + "objective",
                new XAttribute("objectiveID", o.Id!),
                o.MapList.Where(IsExtended).Select(m =>
                {
                    var mapInfo = new XElement(Adlseq + "mapInfo", new XAttribute("targetObjectiveID", m.TargetObjectiveId));
                    if (!m.ReadRawScore) mapInfo.Add(new XAttribute("readRawScore", "false"));
                    if (!m.ReadMinScore) mapInfo.Add(new XAttribute("readMinScore", "false"));
                    if (!m.ReadMaxScore) mapInfo.Add(new XAttribute("readMaxScore", "false"));
                    if (!m.ReadCompletionStatus) mapInfo.Add(new XAttribute("readCompletionStatus", "false"));
                    if (!m.ReadProgressMeasure) mapInfo.Add(new XAttribute("readProgressMeasure", "false"));
                    if (m.WriteRawScore) mapInfo.Add(new XAttribute("writeRawScore", "true"));
                    if (m.WriteMinScore) mapInfo.Add(new XAttribute("writeMinScore", "true"));
                    if (m.WriteMaxScore) mapInfo.Add(new XAttribute("writeMaxScore", "true"));
                    if (m.WriteCompletionStatus) mapInfo.Add(new XAttribute("writeCompletionStatus", "true"));
                    if (m.WriteProgressMeasure) mapInfo.Add(new XAttribute("writeProgressMeasure", "true"));
                    return mapInfo;
                })))));

        return element.HasElements ? element : null;
    }

    /// <summary>Ánh xạ chỉ có ý nghĩa ở adlseq:mapInfo — những gì imsss:mapInfo không diễn tả được.</summary>
    private static bool IsExtended(ObjectiveMapDefinition m)
        => m.WriteRawScore || m.WriteMinScore || m.WriteMaxScore || m.WriteCompletionStatus || m.WriteProgressMeasure
           || (!m.ReadSatisfiedStatus && !m.ReadNormalizedMeasure && !m.WriteSatisfiedStatus && !m.WriteNormalizedMeasure
               && (m.ReadRawScore || m.ReadCompletionStatus || m.ReadProgressMeasure));

    private static XElement WriteRule(string name, SequencingRuleDefinition rule)
    {
        var conditions = new XElement(Imsss + "ruleConditions");
        if (rule.ConditionCombination != "all") conditions.Add(new XAttribute("conditionCombination", rule.ConditionCombination));
        foreach (var condition in rule.ConditionList)
        {
            var c = new XElement(Imsss + "ruleCondition");
            if (condition.ReferencedObjective is string referenced) c.Add(new XAttribute("referencedObjective", referenced));
            if (condition.Condition is "objectiveMeasureGreaterThan" or "objectiveMeasureLessThan")
                c.Add(new XAttribute("measureThreshold", Num(condition.MeasureThreshold)));
            if (condition.Operator != "noOp") c.Add(new XAttribute("operator", condition.Operator));
            c.Add(new XAttribute("condition", condition.Condition));
            conditions.Add(c);
        }
        return new XElement(Imsss + name, conditions, new XElement(Imsss + "ruleAction", new XAttribute("action", rule.Action)));
    }

    // ==================== TIỆN ÍCH ====================

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static bool IsAbsolute(string href) => href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>xs:ID không được bắt đầu bằng số và không có khoảng trắng / ký tự lạ.</summary>
    public static string SafeId(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var id = new string(chars);
        return id.Length == 0 || !(char.IsLetter(id[0]) || id[0] == '_') ? "_" + id : id;
    }

    private static (string Href, string? Parameters) SplitHref(string href)
    {
        var cut = href.IndexOfAny(new[] { '?', '#' });
        return cut < 0 ? (href, null) : (href[..cut], href[cut..]);
    }
}
