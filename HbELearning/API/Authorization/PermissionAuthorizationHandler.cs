// File: API/Authorization/PermissionAuthorizationHandler.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace HbELearning.API.Authorization;

/// <summary>
/// Cho phép quyền do Admin cấu hình ở màn hình "Vai trò" thỏa mãn các [Authorize(Roles = ...)] đang gắn cứng
/// trong Controller — nhờ vậy một vai trò mới do Admin tự tạo vẫn gọi được API mà không phải sửa lại
/// từng attribute trên hơn 20 Controller.
///
/// Nguyên tắc an toàn:
/// 1. Handler chỉ THÊM quyền, không bao giờ bớt. Không có gì đang chạy được bị chặn lại.
/// 2. Chỉ áp dụng cho vai trò do Admin tạo (IsSystem = false). Sáu vai trò cài sẵn giữ nguyên 100% hành vi cũ.
/// 3. Các bước trong chuỗi duyệt khóa học (Submit/Approve/Reject/ChangeStatus) không bao giờ mở theo quyền —
///    chúng phụ thuộc đúng vai trò nào đang duyệt nên vẫn chặn theo tên vai trò.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<RolesAuthorizationRequirement>
{

    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        // Vai trò cài sẵn: giữ nguyên cách chặn cũ, không can thiệp.
        if (context.User.FindFirst(AppClaimType.SystemRoleClaimType)?.Value != "false")
            return Task.CompletedTask;

        var required = ResolveRequiredPermission(context);
        if (required is null) return Task.CompletedTask;

        // Quyền nằm trong 1 claim duy nhất dạng CSV để token không phình ra vì hàng chục claim rời.
        var grantedList = FunctionKey.Parse(context.User.FindFirst(AppClaimType.PermissionClaimType)?.Value);
        if (grantedList.Contains(required)) context.Succeed(requirement);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Suy ra quyền cần có cho request hiện tại: chức năng lấy theo tên Controller, thao tác lấy theo HTTP method.
    /// Trả null nếu request không nằm trong phạm vi áp dụng cơ chế quyền.
    /// </summary>
    private string? ResolveRequiredPermission(AuthorizationHandlerContext context)
    {
        var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
        var descriptor = httpContext?.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (httpContext is null || descriptor is null) return null;

        if (FunctionKey.ApprovalActionList.Contains(descriptor.ActionName)) return null;
        if (!FunctionKey.ModuleByController.TryGetValue(descriptor.ControllerName, out var module)) return null;

        var action = FunctionKey.ActionFromHttpMethod(httpContext.Request.Method);
        return action is null ? null : module + "." + action;
    }
}
