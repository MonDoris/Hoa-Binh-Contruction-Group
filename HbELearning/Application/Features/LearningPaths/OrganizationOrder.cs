// File: Application/Features/LearningPaths/OrganizationOrder.cs

namespace HbELearning.Application.Features.LearningPaths;

/// <summary>Một nút của cấu trúc khóa học sau khi dựng cây từ danh sách PathItem phẳng.</summary>
public sealed class OrganizationNode
{
    public required PathItem Item { get; init; }
    public OrganizationNode? Parent { get; init; }
    public List<OrganizationNode> Children { get; } = new();
    public int Depth { get; init; }
    public bool IsModule => Item.ItemType == PathItemType.Module;
}

/// <summary>
/// Dựng cây Chương / Mục từ các PathItem của một lộ trình (= SCORM Organization) và duyệt theo đúng thứ tự
/// "forward" của chuẩn (tiền thứ tự: chương, rồi các mục trong chương, rồi chương kế tiếp).
///
/// Mọi nơi cần THỨ TỰ HỌC — trình phát (qua cây hoạt động), lộ trình / mở khóa ở LearningService, gói xuất — đều
/// đi qua đây, để không có chỗ nào sắp theo OrderIndex phẳng mà quên mất mục nằm trong chương.
/// </summary>
public static class OrganizationOrder
{
    public static List<OrganizationNode> BuildTree(IEnumerable<PathItem> itemList)
    {
        var ordered = itemList.OrderBy(x => x.OrderIndex).ThenBy(x => x.Id).ToList();
        var moduleIdSet = ordered.Where(x => x.ItemType == PathItemType.Module).Select(x => x.Id).ToHashSet();

        // Mục trỏ tới chương không còn (hoặc trỏ tới một mục lá) thì coi như nằm ở gốc — không để rơi mất nội dung.
        int? ParentOf(PathItem item) => item.ParentItemId is int parentId && moduleIdSet.Contains(parentId) && parentId != item.Id
            ? parentId : null;

        var rootList = new List<OrganizationNode>();
        var visited = new HashSet<int>();

        void Attach(PathItem item, OrganizationNode? parent, List<OrganizationNode> target, int depth)
        {
            if (!visited.Add(item.Id)) return; // chặn vòng lặp cha-con do dữ liệu hỏng
            var node = new OrganizationNode { Item = item, Parent = parent, Depth = depth };
            target.Add(node);
            if (item.ItemType != PathItemType.Module) return;
            foreach (var child in ordered.Where(x => ParentOf(x) == item.Id))
                Attach(child, node, node.Children, depth + 1);
        }

        foreach (var item in ordered.Where(x => ParentOf(x) is null))
            Attach(item, null, rootList, 1);
        return rootList;
    }

    /// <summary>Mọi nút theo tiền thứ tự.</summary>
    public static IEnumerable<OrganizationNode> Preorder(IEnumerable<OrganizationNode> rootList)
    {
        foreach (var node in rootList)
        {
            yield return node;
            foreach (var child in Preorder(node.Children)) yield return child;
        }
    }

    /// <summary>Chỉ các mục LÁ (có nội dung để học) theo đúng thứ tự học.</summary>
    public static List<PathItem> FlattenLeaves(IEnumerable<PathItem> itemList)
        => Preorder(BuildTree(itemList)).Where(n => !n.IsModule).Select(n => n.Item).ToList();
}
