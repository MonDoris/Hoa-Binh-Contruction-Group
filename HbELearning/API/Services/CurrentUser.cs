// File: API/Services/CurrentUser.cs
using System.Security.Claims;

namespace HbELearning.API.Services;

/// <summary>
/// Trả lời "ai đang thao tác" bằng claim trong thẻ JWT của yêu cầu HTTP hiện tại.
///
/// Nằm ở tầng API vì nó đọc HttpContext — thứ chỉ tồn tại ở tầng web. Nội dung chép nguyên từ các thuộc tính
/// CurrentUserId / IsAdmin... từng nằm trong BaseApiController, nên cách hiểu quyền không đổi.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public int UserId =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public string? FullName => Principal?.FindFirstValue(ClaimTypes.Name);

    public int? DepartmentId =>
        int.TryParse(Principal?.FindFirstValue("departmentId"), out var id) ? id : null;

    public int? PositionId =>
        int.TryParse(Principal?.FindFirstValue("positionId"), out var id) ? id : null;

    // Kiểm tra qua IsInRole chứ không so bằng Role: một vai trò kế thừa mang HAI claim vai trò — tên thật của nó
    // và tên vai trò cha (xem TokenService). So bằng chỉ thấy cái đầu tiên, nên "Trưởng phòng IT" sẽ không được
    // coi là Trưởng phòng và mất sạch những gì vừa kế thừa.
    public bool IsAdmin => IsInRole(RoleName.Admin);
    public bool IsTrainer => IsInRole(RoleName.Trainer);
    public bool IsHeadOfDepartment => IsInRole(RoleName.HeadOfDepartment);
    public bool IsDirector => IsInRole(RoleName.Director);
    public bool IsManager => IsAdmin || IsTrainer;

    /// <summary>Các cấp này phải đọc được nội dung thì mới duyệt được — trước đây họ rơi vào nhánh "học viên",
    /// bị đòi bản ghi phân công học và nhận 403. Khớp RoleName.CourseViewers dùng cho [Authorize].</summary>
    public bool IsContentViewer => IsManager || IsHeadOfDepartment || IsDirector;

    public bool IsInRole(string roleName) => Principal?.IsInRole(roleName) ?? false;

    public string? FindClaim(string claimType) => Principal?.FindFirst(claimType)?.Value;
}
