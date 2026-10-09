// File: Application/Features/Scorm/Manifest/ScormManifestParser.cs
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using HbELearning.Application.Features.Scorm.Model;

namespace HbELearning.Application.Features.Scorm.Manifest;

/// <summary>Gói không hợp lệ — thông điệp viết cho người soạn bài đọc, không phải cho lập trình viên.</summary>
public sealed class ScormPackageException : Exception
{
    public ScormPackageException(string message) : base(message) { }
}

/// <summary>
/// Đọc imsmanifest.xml (IMS Content Packaging 1.1.x + phần mở rộng ADL) thành <see cref="ScormActivityTree"/>.
///
/// Đọc theo TÊN CỤC BỘ của thẻ, không theo namespace: ngoài đời có rất nhiều gói khai namespace lệch chuẩn (thiếu
/// hẳn, sai phiên bản schema, dùng tiền tố lạ) mà vẫn chạy được trên các LMS lớn. Bám cứng namespace thì những gói
/// đó bị từ chối chỉ vì một chi tiết khai báo — trong khi cấu trúc bên trong vẫn đúng.
///
/// Không bao giờ tải DTD hay tài nguyên ngoài (XXE): manifest đến từ tệp người dùng tải lên.
/// </summary>
public static class ScormManifestParser
{
    /// <param name="manifestXml">Nội dung imsmanifest.xml.</param>
    /// <param name="fileExists">Hỏi một đường dẫn tương đối (tính từ gốc gói) có tệp thật không — để báo ngay lúc
    /// nhập nếu gói trỏ tới trang khởi chạy không tồn tại, thay vì để học viên gặp trang trắng.</param>
    public static ScormActivityTree Parse(string manifestXml, Func<string, bool> fileExists)
    {
        var document = LoadSafely(manifestXml);
        var manifest = document.Root;
        if (manifest is null || manifest.Name.LocalName != "manifest")
            throw new ScormPackageException("imsmanifest.xml không có thẻ gốc <manifest>.");

        var schemaVersion = Child(Child(manifest, "metadata"), "schemaversion")?.Value.Trim();
        var version = DetectVersion(manifest, schemaVersion);

        var tree = new ScormActivityTree
        {
            Version = version,
            SchemaVersion = string.IsNullOrEmpty(schemaVersion) ? null : Truncate(schemaVersion, 50),
            ManifestIdentifier = Attr(manifest, "identifier") is string identifier ? Truncate(identifier, 250) : null,
        };

        // ---------- Tài nguyên ----------
        var manifestBase = Attr(manifest, "base") ?? "";
        var resources = Child(manifest, "resources");
        var resourcesBase = resources is null ? "" : Attr(resources, "base") ?? "";
        var resourceById = new Dictionary<string, (string? Href, bool IsSco)>(StringComparer.Ordinal);
        foreach (var resource in Children(resources, "resource"))
        {
            var id = Attr(resource, "identifier");
            if (string.IsNullOrEmpty(id) || resourceById.ContainsKey(id)) continue;

            var href = Attr(resource, "href");
            var resolved = string.IsNullOrWhiteSpace(href)
                ? null
                : CombineBase(manifestBase + resourcesBase + (Attr(resource, "base") ?? ""), href.Trim());
            // 1.2: adlcp:scormtype, 2004: adlcp:scormType. Thiếu hẳn thì coi là asset — đúng mặc định của chuẩn.
            var scormType = (Attr(resource, "scormType") ?? Attr(resource, "scormtype") ?? "asset").Trim();
            resourceById[id] = (resolved, scormType.Equals("sco", StringComparison.OrdinalIgnoreCase));
        }

        // ---------- Bộ sequencing dùng chung (imsss:sequencingCollection) ----------
        var collectionById = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var sequencing in Children(Child(manifest, "sequencingCollection"), "sequencing"))
            if (Attr(sequencing, "ID") is string sequencingId && !collectionById.ContainsKey(sequencingId))
                collectionById[sequencingId] = sequencing;

        // ---------- Tổ chức mặc định ----------
        var organizations = Child(manifest, "organizations")
            ?? throw new ScormPackageException("Gói không có thẻ <organizations> — không có gì để học.");
        var organizationList = Children(organizations, "organization").ToList();
        if (organizationList.Count == 0)
            throw new ScormPackageException("Gói không có <organization> nào — không có gì để học.");

        var defaultId = Attr(organizations, "default");
        var organization = organizationList.FirstOrDefault(x => Attr(x, "identifier") == defaultId) ?? organizationList[0];

        tree.OrganizationId = Attr(organization, "identifier") ?? "ORG";
        tree.ObjectivesGlobalToSystem = ParseBool(Attr(organization, "objectivesGlobalToSystem"), true);
        tree.SharedDataGlobalToSystem = ParseBool(Attr(organization, "sharedDataGlobalToSystem"), true);

        var context = new ParseContext(version, resourceById, collectionById, fileExists);
        var root = new ScormActivityDefinition
        {
            Id = tree.OrganizationId,
            Title = Truncate(Child(organization, "title")?.Value.Trim() is { Length: > 0 } title ? title : "Nội dung SCORM", 250),
            Sequencing = ParseSequencing(Child(organization, "sequencing"), context, isRoot: true),
        };
        foreach (var item in Children(organization, "item"))
            root.Children.Add(ParseItem(item, context));

        if (root.Children.Count == 0)
            throw new ScormPackageException("Tổ chức mặc định của gói không có mục (<item>) nào.");

        tree.Root = root;
        ValidateTree(tree);
        return tree;
    }

    private sealed record ParseContext(
        ScormVersion Version,
        Dictionary<string, (string? Href, bool IsSco)> ResourceById,
        Dictionary<string, XElement> CollectionById,
        Func<string, bool> FileExists)
    {
        public HashSet<string> SeenIdSet { get; } = new(StringComparer.Ordinal);
    }

    // ==================== ITEM ====================

    private static ScormActivityDefinition ParseItem(XElement item, ParseContext context)
    {
        var id = Attr(item, "identifier");
        if (string.IsNullOrWhiteSpace(id))
            throw new ScormPackageException("Có <item> thiếu thuộc tính identifier.");
        if (!context.SeenIdSet.Add(id))
            throw new ScormPackageException($"identifier \"{id}\" bị trùng giữa hai <item>.");

        var activity = new ScormActivityDefinition
        {
            Id = Truncate(id, 250),
            Title = Truncate(Child(item, "title")?.Value.Trim() is { Length: > 0 } title ? title : id, 250),
            IsVisible = ParseBool(Attr(item, "isvisible"), true),
        };

        foreach (var child in Children(item, "item"))
            activity.Children.Add(ParseItem(child, context));

        // Chỉ hoạt động LÁ mới mở nội dung — identifierref trên một cụm bị chuẩn bỏ qua.
        if (activity.IsLeaf)
        {
            var resourceId = Attr(item, "identifierref");
            if (string.IsNullOrEmpty(resourceId) || !context.ResourceById.TryGetValue(resourceId, out var resource))
                throw new ScormPackageException($"Mục \"{activity.Title}\" không trỏ tới tài nguyên (resource) nào hợp lệ.");
            if (string.IsNullOrEmpty(resource.Href))
                throw new ScormPackageException($"Tài nguyên \"{resourceId}\" của mục \"{activity.Title}\" không có href.");

            activity.ResourceId = resourceId;
            activity.IsSco = resource.IsSco;
            activity.LaunchUrl = AppendParameters(resource.Href, Attr(item, "parameters"));

            if (!IsAbsoluteUrl(resource.Href))
            {
                var path = StripQueryAndFragment(resource.Href);
                string decoded;
                try { decoded = Uri.UnescapeDataString(path); } catch { decoded = path; }
                if (!context.FileExists(decoded) && !context.FileExists(path))
                    throw new ScormPackageException($"Tệp khởi chạy \"{path}\" của mục \"{activity.Title}\" không có trong gói.");
            }
        }

        // ----- Phần mở rộng ADL trên item -----
        activity.DataFromLms = Child(item, "dataFromLMS")?.Value ?? Child(item, "datafromlms")?.Value;
        activity.TimeLimitAction = NormalizeTimeLimitAction(Child(item, "timeLimitAction")?.Value ?? Child(item, "timelimitaction")?.Value);

        if (context.Version == ScormVersion.Scorm12)
        {
            activity.MaxTimeAllowed = Child(item, "maxtimeallowed")?.Value.Trim();
            if (decimal.TryParse(Child(item, "masteryscore")?.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mastery))
                activity.MasteryScore = mastery;
        }

        var threshold = Child(item, "completionThreshold");
        if (threshold is not null)
        {
            // 4th Ed: thuộc tính completedByMeasure / minProgressMeasure / progressWeight.
            // 3rd Ed: con số nằm ngay trong thẻ — nghĩa tương đương "hoàn thành khi đạt ngưỡng này".
            var text = threshold.Value.Trim();
            if (text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var legacy))
                activity.CompletionThreshold = new CompletionThresholdDefinition { CompletedByMeasure = true, MinProgressMeasure = Clamp01(legacy) };
            else
                activity.CompletionThreshold = new CompletionThresholdDefinition
                {
                    CompletedByMeasure = ParseBool(Attr(threshold, "completedByMeasure"), false),
                    MinProgressMeasure = Clamp01(ParseDouble(Attr(threshold, "minProgressMeasure"), 1.0)),
                    ProgressWeight = Clamp01(ParseDouble(Attr(threshold, "progressWeight"), 1.0)),
                };
        }

        foreach (var map in Children(Child(item, "data"), "map"))
        {
            var target = Attr(map, "targetID");
            if (string.IsNullOrWhiteSpace(target)) continue;
            activity.SharedDataList.Add(new SharedDataMapDefinition
            {
                TargetId = target,
                ReadSharedData = ParseBool(Attr(map, "readSharedData"), true),
                WriteSharedData = ParseBool(Attr(map, "writeSharedData"), true),
            });
        }

        var navigationInterface = Child(Child(item, "presentation"), "navigationInterface");
        foreach (var hide in Children(navigationInterface, "hideLMSUI"))
        {
            var value = hide.Value.Trim();
            if (value.Length > 0 && !activity.HideLmsUi.Contains(value)) activity.HideLmsUi.Add(value);
        }

        activity.Sequencing = ParseSequencing(Child(item, "sequencing"), context, isRoot: false);

        // 2004 lấy giới hạn thời gian từ limitConditions; 1.2 đã đọc ở trên.
        if (context.Version == ScormVersion.Scorm2004)
            activity.MaxTimeAllowed = activity.Sequencing.AttemptAbsoluteDurationLimit;

        return activity;
    }

    // ==================== SEQUENCING ====================

    private static SequencingDefinition ParseSequencing(XElement? local, ParseContext context, bool isRoot)
    {
        var definition = new SequencingDefinition();

        // SCORM 1.2 không có sequencing: cho học viên tự chọn từ mục lục VÀ bấm Tiếp / Trước — đúng cách các LMS
        // 1.2 vẫn phát gói nhiều SCO.
        if (context.Version == ScormVersion.Scorm12)
        {
            definition.ControlChoice = true;
            definition.ControlFlow = true;
            return definition;
        }

        if (local is null) return definition;

        // IDRef: bộ dùng chung làm nền, phần khai tại chỗ ĐÈ lên theo từng nhóm thẻ (controlMode, sequencingRules...).
        var groupByName = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        if (Attr(local, "IDRef") is string idRef && context.CollectionById.TryGetValue(idRef, out var shared))
            foreach (var element in shared.Elements())
                AddGroup(groupByName, element);

        var localGroup = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        foreach (var element in local.Elements()) AddGroup(localGroup, element);
        foreach (var (name, list) in localGroup) groupByName[name] = list;

        XElement? Group(string name) => groupByName.TryGetValue(name, out var list) ? list[0] : null;
        IEnumerable<XElement> GroupAll(string name) => groupByName.TryGetValue(name, out var list) ? list : Enumerable.Empty<XElement>();

        if (Group("controlMode") is XElement controlMode)
        {
            definition.ControlChoice = ParseBool(Attr(controlMode, "choice"), true);
            definition.ControlChoiceExit = ParseBool(Attr(controlMode, "choiceExit"), true);
            definition.ControlFlow = ParseBool(Attr(controlMode, "flow"), false);
            definition.ControlForwardOnly = ParseBool(Attr(controlMode, "forwardOnly"), false);
            definition.UseCurrentAttemptObjectiveInfo = ParseBool(Attr(controlMode, "useCurrentAttemptObjectiveInfo"), true);
            definition.UseCurrentAttemptProgressInfo = ParseBool(Attr(controlMode, "useCurrentAttemptProgressInfo"), true);
        }

        foreach (var rules in GroupAll("sequencingRules"))
        {
            foreach (var rule in Children(rules, "preConditionRule")) definition.PreConditionRuleList.Add(ParseSequencingRule(rule));
            foreach (var rule in Children(rules, "exitConditionRule")) definition.ExitConditionRuleList.Add(ParseSequencingRule(rule));
            foreach (var rule in Children(rules, "postConditionRule")) definition.PostConditionRuleList.Add(ParseSequencingRule(rule));
        }

        if (Group("limitConditions") is XElement limit)
        {
            if (int.TryParse(Attr(limit, "attemptLimit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var attemptLimit) && attemptLimit > 0)
                definition.AttemptLimit = attemptLimit;
            definition.AttemptAbsoluteDurationLimit = Attr(limit, "attemptAbsoluteDurationLimit");
            if (DateTime.TryParse(Attr(limit, "beginTimeLimit"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out var begin))
                definition.BeginTimeLimit = begin.ToLocalTime();
            if (DateTime.TryParse(Attr(limit, "endTimeLimit"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out var end))
                definition.EndTimeLimit = end.ToLocalTime();
        }

        if (Group("rollupRules") is XElement rollupRules)
        {
            definition.RollupObjectiveSatisfied = ParseBool(Attr(rollupRules, "rollupObjectiveSatisfied"), true);
            definition.RollupProgressCompletion = ParseBool(Attr(rollupRules, "rollupProgressCompletion"), true);
            definition.ObjectiveMeasureWeight = Clamp01(ParseDouble(Attr(rollupRules, "objectiveMeasureWeight"), 1.0));
            foreach (var rule in Children(rollupRules, "rollupRule"))
            {
                var parsed = new RollupRuleDefinition
                {
                    ChildActivitySet = Attr(rule, "childActivitySet") ?? "all",
                    MinimumCount = int.TryParse(Attr(rule, "minimumCount"), out var count) ? Math.Max(0, count) : 0,
                    MinimumPercent = Clamp01(ParseDouble(Attr(rule, "minimumPercent"), 0)),
                    Action = Attr(Child(rule, "rollupAction"), "action") ?? "",
                };
                var conditions = Child(rule, "rollupConditions");
                parsed.ConditionCombination = Attr(conditions, "conditionCombination") ?? "any";
                foreach (var condition in Children(conditions, "rollupCondition"))
                    parsed.ConditionList.Add(new RollupConditionDefinition
                    {
                        Condition = Attr(condition, "condition") ?? "",
                        Operator = Attr(condition, "operator") ?? "noOp",
                    });
                if (parsed.Action.Length > 0) definition.RollupRuleList.Add(parsed);
            }
        }

        if (Group("rollupConsiderations") is XElement considerations)
        {
            definition.RequiredForSatisfied = Attr(considerations, "requiredForSatisfied") ?? "always";
            definition.RequiredForNotSatisfied = Attr(considerations, "requiredForNotSatisfied") ?? "always";
            definition.RequiredForCompleted = Attr(considerations, "requiredForCompleted") ?? "always";
            definition.RequiredForIncomplete = Attr(considerations, "requiredForIncomplete") ?? "always";
            definition.MeasureSatisfactionIfActive = ParseBool(Attr(considerations, "measureSatisfactionIfActive"), true);
        }

        // imsss:objectives (namespace imsss) và adlseq:objectives (namespace adlseq) trùng TÊN CỤC BỘ — phân biệt
        // bằng thẻ con: bản imsss có primaryObjective / objective kèm minNormalizedMeasure, bản adlseq chỉ có
        // objective kèm adlseq:mapInfo với các cờ raw/min/max/completion/progress.
        foreach (var objectives in GroupAll("objectives"))
        {
            if (IsAdlseq(objectives)) continue;
            if (Child(objectives, "primaryObjective") is XElement primary)
                definition.ObjectiveList.Add(ParseObjective(primary, isPrimary: true));
            foreach (var objective in Children(objectives, "objective"))
                definition.ObjectiveList.Add(ParseObjective(objective, isPrimary: false));
        }
        foreach (var objectives in GroupAll("objectives").Where(IsAdlseq))
            foreach (var objective in Children(objectives, "objective"))
            {
                var target = definition.ObjectiveList.FirstOrDefault(x => x.Id == Attr(objective, "objectiveID"));
                if (target is null) continue;
                foreach (var map in Children(objective, "mapInfo"))
                {
                    var targetId = Attr(map, "targetObjectiveID");
                    if (string.IsNullOrEmpty(targetId)) continue;
                    var existing = target.MapList.FirstOrDefault(x => x.TargetObjectiveId == targetId);
                    if (existing is null)
                    {
                        // Ánh xạ chỉ khai ở adlseq: phần đọc/ghi trạng thái đạt và điểm theo mặc định "không đụng tới".
                        existing = new ObjectiveMapDefinition
                        {
                            TargetObjectiveId = targetId,
                            ReadSatisfiedStatus = false,
                            ReadNormalizedMeasure = false,
                        };
                        target.MapList.Add(existing);
                    }
                    existing.ReadRawScore = ParseBool(Attr(map, "readRawScore"), true);
                    existing.ReadMinScore = ParseBool(Attr(map, "readMinScore"), true);
                    existing.ReadMaxScore = ParseBool(Attr(map, "readMaxScore"), true);
                    existing.ReadCompletionStatus = ParseBool(Attr(map, "readCompletionStatus"), true);
                    existing.ReadProgressMeasure = ParseBool(Attr(map, "readProgressMeasure"), true);
                    existing.WriteRawScore = ParseBool(Attr(map, "writeRawScore"), false);
                    existing.WriteMinScore = ParseBool(Attr(map, "writeMinScore"), false);
                    existing.WriteMaxScore = ParseBool(Attr(map, "writeMaxScore"), false);
                    existing.WriteCompletionStatus = ParseBool(Attr(map, "writeCompletionStatus"), false);
                    existing.WriteProgressMeasure = ParseBool(Attr(map, "writeProgressMeasure"), false);
                }
            }

        if (Group("randomizationControls") is XElement randomization)
        {
            definition.RandomizationTiming = Attr(randomization, "randomizationTiming") ?? "never";
            definition.SelectionTiming = Attr(randomization, "selectionTiming") ?? "never";
            definition.ReorderChildren = ParseBool(Attr(randomization, "reorderChildren"), false);
            if (int.TryParse(Attr(randomization, "selectCount"), out var selectCount) && selectCount >= 0)
                definition.SelectCount = selectCount;
        }

        if (Group("deliveryControls") is XElement delivery)
        {
            definition.Tracked = ParseBool(Attr(delivery, "tracked"), true);
            definition.CompletionSetByContent = ParseBool(Attr(delivery, "completionSetByContent"), false);
            definition.ObjectiveSetByContent = ParseBool(Attr(delivery, "objectiveSetByContent"), false);
        }

        if (Group("constrainedChoiceConsiderations") is XElement constrained)
        {
            definition.PreventActivation = ParseBool(Attr(constrained, "preventActivation"), false);
            definition.ConstrainChoice = ParseBool(Attr(constrained, "constrainChoice"), false);
        }

        return definition;

        static void AddGroup(Dictionary<string, List<XElement>> map, XElement element)
        {
            var name = element.Name.LocalName;
            if (!map.TryGetValue(name, out var list)) map[name] = list = new List<XElement>();
            list.Add(element);
        }
    }

    private static bool IsAdlseq(XElement objectives)
        => objectives.Name.NamespaceName.Contains("adlseq", StringComparison.OrdinalIgnoreCase)
           || (Child(objectives, "primaryObjective") is null
               && Children(objectives, "objective").Any(o => Children(o, "mapInfo").Any(m =>
                   Attr(m, "readRawScore") is not null || Attr(m, "writeRawScore") is not null
                   || Attr(m, "readCompletionStatus") is not null || Attr(m, "writeCompletionStatus") is not null
                   || Attr(m, "readProgressMeasure") is not null || Attr(m, "writeProgressMeasure") is not null
                   || Attr(m, "readMinScore") is not null || Attr(m, "readMaxScore") is not null)));

    private static SequencingRuleDefinition ParseSequencingRule(XElement rule)
    {
        var conditions = Child(rule, "ruleConditions");
        var parsed = new SequencingRuleDefinition
        {
            ConditionCombination = Attr(conditions, "conditionCombination") ?? "all",
            Action = Attr(Child(rule, "ruleAction"), "action") ?? "",
        };
        foreach (var condition in Children(conditions, "ruleCondition"))
            parsed.ConditionList.Add(new RuleConditionDefinition
            {
                Condition = Attr(condition, "condition") ?? "",
                ReferencedObjective = Attr(condition, "referencedObjective"),
                MeasureThreshold = ParseDouble(Attr(condition, "measureThreshold"), 0),
                Operator = Attr(condition, "operator") ?? "noOp",
            });
        return parsed;
    }

    private static ObjectiveDefinition ParseObjective(XElement element, bool isPrimary)
    {
        var objective = new ObjectiveDefinition
        {
            Id = Attr(element, "objectiveID"),
            IsPrimary = isPrimary,
            SatisfiedByMeasure = ParseBool(Attr(element, "satisfiedByMeasure"), false),
            MinNormalizedMeasure = Math.Clamp(ParseDouble(Child(element, "minNormalizedMeasure")?.Value, 1.0), -1, 1),
        };
        foreach (var map in Children(element, "mapInfo"))
        {
            var targetId = Attr(map, "targetObjectiveID");
            if (string.IsNullOrEmpty(targetId)) continue;
            objective.MapList.Add(new ObjectiveMapDefinition
            {
                TargetObjectiveId = targetId,
                ReadSatisfiedStatus = ParseBool(Attr(map, "readSatisfiedStatus"), true),
                ReadNormalizedMeasure = ParseBool(Attr(map, "readNormalizedMeasure"), true),
                WriteSatisfiedStatus = ParseBool(Attr(map, "writeSatisfiedStatus"), false),
                WriteNormalizedMeasure = ParseBool(Attr(map, "writeNormalizedMeasure"), false),
            });
        }
        return objective;
    }

    // ==================== KIỂM TRA SAU CÙNG ====================

    private static void ValidateTree(ScormActivityTree tree)
    {
        var leafList = tree.Preorder().Where(x => x.IsLeaf && x != tree.Root).ToList();
        if (leafList.Count == 0)
            throw new ScormPackageException("Gói không có mục nào mở được nội dung.");
    }

    // ==================== TIỆN ÍCH XML ====================

    private static XDocument LoadSafely(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new ScormPackageException($"imsmanifest.xml không đúng cú pháp XML (dòng {ex.LineNumber}): {ex.Message}");
        }
    }

    private static ScormVersion DetectVersion(XElement manifest, string? schemaVersion)
    {
        if (!string.IsNullOrEmpty(schemaVersion))
        {
            if (schemaVersion.Contains("1.2")) return ScormVersion.Scorm12;
            if (schemaVersion.Contains("2004", StringComparison.OrdinalIgnoreCase)
                || schemaVersion.Contains("CAM 1.3", StringComparison.OrdinalIgnoreCase))
                return ScormVersion.Scorm2004;
        }

        // Thiếu schemaversion: nhìn namespace đã khai trên toàn tài liệu.
        var namespaceList = manifest.DescendantsAndSelf()
            .SelectMany(e => e.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Value))
            .ToList();
        if (namespaceList.Any(n => n.Contains("adlcp_rootv1p2", StringComparison.OrdinalIgnoreCase)
                                || n.Contains("imscp_rootv1p1p2", StringComparison.OrdinalIgnoreCase)))
            return ScormVersion.Scorm12;
        if (namespaceList.Any(n => n.Contains("v1p3", StringComparison.OrdinalIgnoreCase)
                                || n.Contains("imsss", StringComparison.OrdinalIgnoreCase)))
            return ScormVersion.Scorm2004;

        return ScormVersion.Scorm12;
    }

    private static XElement? Child(XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Children(XElement? parent, string localName)
        => parent is null ? Enumerable.Empty<XElement>() : parent.Elements().Where(e => e.Name.LocalName == localName);

    /// <summary>Thuộc tính theo tên cục bộ (bỏ qua tiền tố namespace) — riêng "base" thì tìm xml:base.</summary>
    private static string? Attr(XElement? element, string localName)
    {
        if (element is null) return null;
        if (localName == "base")
            return element.Attribute(XNamespace.Xml + "base")?.Value;
        var value = element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && a.Name.LocalName == localName)?.Value;
        return value?.Trim();
    }

    private static bool ParseBool(string? value, bool fallback) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => fallback,
    };

    private static double ParseDouble(string? value, double fallback)
        => double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? NormalizeTimeLimitAction(string? value)
    {
        var text = value?.Trim().ToLowerInvariant();
        return text switch
        {
            "exit,message" or "exit,no message" or "continue,message" or "continue,no message" => text,
            _ => null,
        };
    }

    private static bool IsAbsoluteUrl(string href)
        => href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string StripQueryAndFragment(string href)
    {
        var cut = href.IndexOfAny(new[] { '?', '#' });
        return (cut >= 0 ? href[..cut] : href).TrimStart('/');
    }

    /// <summary>Ghép chuỗi xml:base (đã nối manifest → resources → resource) với href.</summary>
    private static string CombineBase(string baseChain, string href)
    {
        if (IsAbsoluteUrl(href) || string.IsNullOrWhiteSpace(baseChain)) return href.TrimStart('/');
        if (IsAbsoluteUrl(baseChain) && Uri.TryCreate(new Uri(baseChain), href, out var absolute)) return absolute.ToString();

        var combined = baseChain.Replace('\\', '/');
        if (!combined.EndsWith('/')) combined += "/";
        return (combined + href).TrimStart('/');
    }

    /// <summary>Nối thuộc tính parameters của item vào href theo CAM: "?a=b" / "a=b" thành truy vấn, "#x" thành neo.</summary>
    private static string AppendParameters(string href, string? parameters)
    {
        var value = parameters?.Trim();
        if (string.IsNullOrEmpty(value)) return href;

        var fragmentIndex = href.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? href[fragmentIndex..] : "";
        var main = fragmentIndex >= 0 ? href[..fragmentIndex] : href;

        if (value.StartsWith('#'))
            return fragment.Length > 0 ? href : main + value;

        value = value.TrimStart('?', '&');
        if (value.Length == 0) return href;
        return main + (main.Contains('?') ? "&" : "?") + value + fragment;
    }
}
