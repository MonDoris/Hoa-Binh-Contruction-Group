// File: Domain/Constants/FunctionKey.cs
namespace HbELearning.Domain.Constants;

/// <summary>
/// Danh mục quyền của hệ thống. Một quyền có dạng "chức năng.thao tác" (VD "user.create") —
/// đúng bằng khóa menu ở wwwroot/js/app.js (MENU) và FUNCTION_ACTION, để màn hình phân quyền
/// và phần kiểm tra ở server luôn nói cùng một ngôn ngữ.
/// </summary>
public static class FunctionKey
{
    public const string View = "view";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Export = "export";

    /// <summary>Thao tác hợp lệ của từng chức năng. Phải khớp FUNCTION_ACTION bên app.js.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ActionByModule = new Dictionary<string, string[]>
    {
        ["dashboard"]    = new[] { View },
        ["my-course"]    = new[] { View },
        ["ai-tutor"]     = new[] { View },
        ["certificate"]  = new[] { View, Export },
        ["ranking"]      = new[] { View, Update },
        ["shop"]         = new[] { View, Create, Update, Delete },
        ["online-class"] = new[] { View, Create, Update, Delete },
        ["course"]       = new[] { View, Create, Update, Delete },
        ["sim-library"]  = new[] { View, Create, Update, Delete },
        ["report"]       = new[] { View, Export },
        ["user"]         = new[] { View, Create, Update, Delete },
        ["organization"] = new[] { View, Create, Update, Delete },
        ["role"]         = new[] { View, Create, Update, Delete },
        ["approval-workflow"] = new[] { View, Create, Update, Delete },
        ["translation"]  = new[] { View, Create, Update, Delete },
        ["ai-document"]  = new[] { View, Create, Delete },
        ["information"]  = new[] { View, Update }
    };

    /// <summary>Toàn bộ khóa quyền hợp lệ, dạng "chức năng.thao tác".</summary>
    public static readonly IReadOnlySet<string> All =
        ActionByModule.SelectMany(pair => pair.Value.Select(action => pair.Key + "." + action)).ToHashSet();

    /// <summary>
    /// Controller nào thuộc chức năng nào — dùng để suy ra quyền cần có cho một request.
    /// Controller không có trong bảng này thì KHÔNG áp dụng cơ chế quyền, chỉ chặn theo vai trò như cũ.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ModuleByController = new Dictionary<string, string>
    {
        ["User"]            = "user",
        ["Department"]      = "organization",
        ["Position"]        = "organization",
        ["Role"]            = "role",
        ["ApprovalWorkflow"] = "approval-workflow",
        ["Translation"]     = "translation",
        ["AiDocument"]      = "ai-document",
        // AiTutor cố ý KHÔNG có mặt ở đây: bảng này chỉ dùng để nới các [Authorize(Roles = ...)] gắn cứng,
        // mà AiTutorService không chặn theo vai trò nào cả — mọi tài khoản đã đăng nhập đều hỏi được,
        // phạm vi tài liệu thì do chính controller rào theo khóa học đã phân công. Khai vào đây sẽ khiến
        // câu hỏi (POST) đòi quyền "ai-tutor.create", một khóa không tồn tại, và chặn nhầm vai trò tự tạo.
        ["Report"]          = "report",
        ["Certificate"]     = "certificate",
        ["Ranking"]         = "ranking",
        ["Shop"]            = "shop",
        ["OnlineClassRoom"] = "online-class",
        ["Course"]          = "course",
        ["Lesson"]          = "course",
        ["Quiz"]            = "course",
        ["LearningPath"]    = "course",
        ["Scenario"]        = "course",
        ["SimAsset"]        = "sim-library",
        ["VideoLearning"]   = "course",
        ["Enrollment"]      = "course",
        ["Import"]          = "course",
        ["Ai"]              = "course"
    };

    /// <summary>
    /// Các action KHÔNG bao giờ mở theo quyền chức năng — chúng thuộc chuỗi duyệt nội dung khóa học,
    /// vốn phụ thuộc đúng vai trò nào đang duyệt (Trưởng phòng -> Giám Đốc -> Giám Đốc Nhân sự).
    /// Cấp "course.create" cho một vai trò mới không được phép biến vai trò đó thành cấp duyệt.
    /// </summary>
    public static readonly IReadOnlySet<string> ApprovalActionList =
        new HashSet<string> { "Submit", "Approve", "Reject", "ChangeStatus" };

    /// <summary>Suy ra thao tác từ HTTP method: GET = xem, POST = thêm, PUT/PATCH = sửa, DELETE = xóa.</summary>
    public static string? ActionFromHttpMethod(string method) => method.ToUpperInvariant() switch
    {
        "GET" or "HEAD" => View,
        "POST" => Create,
        "PUT" or "PATCH" => Update,
        "DELETE" => Delete,
        _ => null
    };

    /// <summary>Dữ liệu cũ lưu khóa trần ("course") được hiểu là quyền xem chức năng đó.</summary>
    private static string Expand(string key) => key.Contains('.') ? key : key + "." + View;

    /// <summary>Chuẩn hóa chuỗi CSV: bỏ khoảng trắng, bỏ trùng, bỏ khóa không có trong danh mục.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var keyList = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Expand)
            .Where(All.Contains)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return keyList.Count == 0 ? null : string.Join(',', keyList);
    }

    /// <summary>Tách chuỗi CSV thành tập khóa. Chuỗi rỗng/null trả về tập rỗng.</summary>
    public static HashSet<string> Parse(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new HashSet<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(Expand).ToHashSet();

    /// <summary>
    /// Quyền của một VAI TRÒ sau khi cộng thêm quyền của vai trò cha.
    ///
    /// Cộng (hợp) chứ không phải thay thế: "Trưởng phòng IT" phải làm được mọi việc của "Trưởng phòng", và
    /// Admin còn tick thêm cho nó vài quyền riêng nữa. Cha không có gì thì con giữ nguyên quyền của mình.
    /// </summary>
    public static string? Inherit(string? ownKeys, string? parentKeys)
    {
        var ownList = Parse(ownKeys);
        var parentList = Parse(parentKeys);
        if (parentList.Count == 0) return Normalize(ownKeys);

        ownList.UnionWith(parentList);
        return Normalize(string.Join(',', ownList));
    }

    /// <summary>
    /// Quyền thực tế của một người: lấy quyền vai trò, rồi GIAO với quyền cấp riêng nếu có.
    /// Vai trò chưa cấu hình quyền (null) thì trả về tập rỗng — nghĩa là không có quyền nào cấp thêm,
    /// người dùng vẫn hoạt động bình thường theo các [Authorize(Roles = ...)] gắn cứng như trước.
    /// </summary>
    public static HashSet<string> Effective(string? roleKeys, string? userKeys)
    {
        var rolePermissionList = Parse(roleKeys);
        if (rolePermissionList.Count == 0) return new HashSet<string>();

        var userPermissionList = Parse(userKeys);
        if (userPermissionList.Count == 0) return rolePermissionList;

        rolePermissionList.IntersectWith(userPermissionList);
        return rolePermissionList;
    }

    /// <summary>
    /// Bổ sung vào bộ quyền đã lưu những khóa mặc định thuộc CHỨC NĂNG mà vai trò chưa có khóa nào —
    /// tức chức năng vừa được thêm vào hệ thống sau lần lưu đó.
    ///
    /// Cố ý KHÔNG hợp nhất toàn bộ bộ mặc định: Admin có thể đã chủ động bỏ bớt một vài thao tác của một
    /// chức năng (VD bỏ "course.delete" của Trưởng Nhóm), gộp lại tất cả sẽ âm thầm trả lại đúng những
    /// quyền họ vừa thu hồi. Chức năng mà vai trò chưa có khóa NÀO thì chắc chắn không phải là lựa chọn
    /// của Admin — vào thời điểm họ cấu hình, chức năng đó còn chưa tồn tại.
    /// </summary>
    public static string AddNewModuleKey(string existingKeys, string roleName)
    {
        var currentList = Parse(existingKeys);
        var knownModuleList = currentList
            .Select(x => x[..x.LastIndexOf('.')])
            .ToHashSet();

        var addedList = Parse(DefaultKeysFor(roleName))
            .Where(x => !knownModuleList.Contains(x[..x.LastIndexOf('.')]))
            .ToList();

        if (addedList.Count == 0) return existingKeys;

        currentList.UnionWith(addedList);
        return Normalize(string.Join(',', currentList)) ?? existingKeys;
    }

    /// <summary>Quyền mặc định của các vai trò cài sẵn — dựng đúng bằng menu mà vai trò đó đang thấy.</summary>
    public static string DefaultKeysFor(string roleName)
    {
        string[] keyList = roleName switch
        {
            RoleName.Admin => new[]
            {
                // Admin chỉ XEM phòng học online — việc tổ chức buổi học thuộc các vai trò nghiệp vụ
                // (xem RoleName.AllExceptAdmin ở OnlineClassRoomService).
                "dashboard.view", "certificate.view", "certificate.export", "ranking.view", "ranking.update",
                "online-class.view",
                // Trên khóa học Admin chỉ XEM, LƯU TRỮ và XÓA — soạn nội dung là việc của Trưởng Nhóm và
                // chuỗi duyệt phía sau (xem RoleName.ContentAuthor). "course.delete" phủ cả nút Xóa lẫn nút
                // Lưu trữ/Bỏ lưu trữ, vốn đã chặn riêng bằng [Authorize(Roles = Admin)] ở ChangeStatus.
                "course.view", "course.delete",
                // Cửa hàng điểm thưởng: Admin quản kho hàng và trao quà, nên có đủ bốn thao tác.
                "shop.view", "shop.create", "shop.update", "shop.delete",
                "sim-library.view", "sim-library.create", "sim-library.update", "sim-library.delete",
                "report.view", "report.export",
                "user.view", "user.create", "user.update", "user.delete",
                "organization.view", "organization.create", "organization.update", "organization.delete",
                "role.view", "role.create", "role.update", "role.delete",
                "approval-workflow.view", "approval-workflow.create", "approval-workflow.update", "approval-workflow.delete",
                "translation.view", "translation.create", "translation.update", "translation.delete",
                "ai-document.view", "ai-document.create", "ai-document.delete",
                "information.view", "information.update"
            },
            RoleName.Trainer => new[]
            {
                "dashboard.view", "my-course.view", "ai-tutor.view", "certificate.view", "certificate.export", "ranking.view", "shop.view",
                "online-class.view", "online-class.create", "online-class.update", "online-class.delete",
                "course.view", "course.create", "course.update", "course.delete",
                "sim-library.view", "sim-library.create", "sim-library.update", "sim-library.delete",
                // Luồng cấp duyệt CỦA PHÒNG BAN MÌNH — phạm vi do ApprovalWorkflowService rào theo phòng
                // ban trong token, quyền ở đây chỉ mở màn hình chứ không cho chạm tới luồng của phòng khác.
                "approval-workflow.view", "approval-workflow.create", "approval-workflow.update", "approval-workflow.delete",
                "report.view", "report.export",
                "information.view", "information.update"
            },
            RoleName.Instructor => new[]
            {
                "dashboard.view", "my-course.view", "ai-tutor.view", "certificate.view", "certificate.export", "ranking.view", "shop.view",
                "online-class.view", "online-class.create", "online-class.update", "online-class.delete",
                "course.view", "course.create", "course.update", "course.delete",
                "sim-library.view", "sim-library.create", "sim-library.update", "sim-library.delete",
                "report.view", "report.export",
                "information.view", "information.update"
            },
            RoleName.HeadOfDepartment or RoleName.Director => new[]
            {
                "dashboard.view", "my-course.view", "ai-tutor.view", "certificate.view", "ranking.view", "shop.view",
                "online-class.view", "online-class.create", "online-class.update", "online-class.delete",
                // Như Trưởng Nhóm, cộng thêm việc xem luồng của phòng ban khác để tham khảo và nhân bản về
                // phòng mình — phần "cấp công ty" này không có khóa quyền riêng, nó đi kèm tên vai trò.
                "approval-workflow.view", "approval-workflow.create", "approval-workflow.update", "approval-workflow.delete",
                "course.view", "report.view", "report.export",
                "information.view", "information.update"
            },
            RoleName.ManagingDirector => new[]
            {
                "dashboard.view", "my-course.view", "ai-tutor.view", "certificate.view", "ranking.view", "shop.view",
                "online-class.view", "online-class.create", "online-class.update", "online-class.delete",
                "report.view", "report.export",
                "information.view", "information.update"
            },
            _ => new[]
            {
                "dashboard.view", "my-course.view", "ai-tutor.view", "certificate.view", "ranking.view", "shop.view",
                "online-class.view", "online-class.create", "online-class.update", "online-class.delete",
                "information.view", "information.update"
            }
        };

        return string.Join(',', keyList);
    }
}
