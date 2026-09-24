// File: Application/Features/Roles/RoleService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Roles;

/// <summary>
/// Quản lý vai trò và quyền của từng vai trò. Sáu vai trò cài sẵn (IsSystem) chỉ được sửa quyền và mô tả —
/// không đổi tên, không xóa, vì tên của chúng được dùng trực tiếp trong luồng duyệt khóa học.
/// </summary>
public class RoleService : IRoleService
{
    private readonly IAppDbContext _db;

    public RoleService(IAppDbContext db)
    {
        _db = db;
    }

    public Result GetFunction()
        =>
        Result.Success(FunctionKey.ActionByModule.Select(pair => new FunctionDto(pair.Key, pair.Value)));

    public async Task<Result> GetList()
    {
        var list = await _db.Role.AsNoTracking()
            .OrderByDescending(x => x.IsSystem).ThenBy(x => x.Id)
            .Select(x => new RoleListDto(x.Id, x.Name, x.Description, x.FunctionKeys,
                x.IsSystem, x.IsActive, x.UserList.Count,
                x.ParentRoleId,
                x.ParentRole != null ? x.ParentRole.Name : null,
                x.ParentRole != null ? x.ParentRole.FunctionKeys : null))
            .ToListAsync();

        return Result.Success(SortByHierarchy(list));
    }

    public async Task<Result> GetById(int id)
    {
        var role = await _db.Role.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new RoleListDto(x.Id, x.Name, x.Description, x.FunctionKeys,
                x.IsSystem, x.IsActive, x.UserList.Count,
                x.ParentRoleId,
                x.ParentRole != null ? x.ParentRole.Name : null,
                x.ParentRole != null ? x.ParentRole.FunctionKeys : null))
            .FirstOrDefaultAsync();

        return role is null ? Result.NotFound(new MessageResponse("Không tìm thấy vai trò.")) : Result.Success(role);
    }

    public async Task<Result> Create(RoleSaveRequest request)
    {
        var name = request.Name.Trim();
        if (await _db.Role.AnyAsync(x => x.Name == name))
            return Result.Conflict(new MessageResponse($"Vai trò \"{name}\" đã tồn tại."));

        var (parentRole, parentError) = await ResolveParentAsync(request.ParentRoleId);
        if (parentError is not null) return Result.Invalid(new MessageResponse(parentError));

        var role = new Role
        {
            Name = name,
            Description = request.Description?.Trim(),
            FunctionKeys = FunctionKey.Normalize(request.FunctionKeys),
            IsActive = request.IsActive,
            IsSystem = false,
            ParentRoleId = parentRole?.Id
        };

        _db.Role.Add(role);
        await _db.SaveChangesAsync();

        return Result.Created(nameof(GetById), new { id = role.Id },
            new RoleListDto(role.Id, role.Name, role.Description, role.FunctionKeys, role.IsSystem, role.IsActive, 0,
                parentRole?.Id, parentRole?.Name, parentRole?.FunctionKeys));
    }

    public async Task<Result> Update(int id, RoleSaveRequest request)
    {
        var role = await _db.Role.FirstOrDefaultAsync(x => x.Id == id);
        if (role is null) return Result.NotFound(new MessageResponse("Không tìm thấy vai trò."));

        var name = request.Name.Trim();

        if (role.IsSystem)
        {
            // Đổi tên vai trò cài sẵn sẽ làm hỏng mọi [Authorize(Roles = ...)] và chuỗi duyệt khóa học.
            if (!string.Equals(role.Name, name, StringComparison.Ordinal))
                return Result.Invalid(new MessageResponse(
                    $"Không thể đổi tên vai trò hệ thống \"{role.Name}\". Chỉ được sửa mô tả và quyền."));

            if (!request.IsActive)
                return Result.Invalid(new MessageResponse("Không thể ngừng hoạt động vai trò hệ thống."));
        }
        else
        {
            if (await _db.Role.AnyAsync(x => x.Name == name && x.Id != id))
                return Result.Conflict(new MessageResponse($"Vai trò \"{name}\" đã tồn tại."));
            role.Name = name;
            role.IsActive = request.IsActive;

            var (parentRole, parentError) = await ResolveParentAsync(request.ParentRoleId);
            if (parentError is not null) return Result.Invalid(new MessageResponse(parentError));
            role.ParentRoleId = parentRole?.Id;
        }

        role.Description = request.Description?.Trim();

        // Bộ quyền của vai trò Admin do hệ thống quy định, không nhận từ form — nếu cho phép bỏ tick, Admin có
        // thể tự khóa mình khỏi chính màn hình phân quyền và không còn đường vào lại ngoài việc sửa thẳng
        // database. Xem FunctionKey.DefaultKeysFor để biết Admin được những gì (trên khóa học chỉ có
        // course.view và course.delete — xem/lưu trữ/xóa, không soạn nội dung).
        role.FunctionKeys = role.Name == RoleName.Admin
            ? FunctionKey.DefaultKeysFor(RoleName.Admin)
            : FunctionKey.Normalize(request.FunctionKeys);

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse(
            "Đã lưu vai trò. Người dùng thuộc vai trò này cần đăng xuất và đăng nhập lại để áp dụng quyền mới."));
    }

    /// <summary>
    /// Kiểm tra vai trò cha được chọn.
    ///
    /// Cha BẮT BUỘC là vai trò hệ thống. Ràng buộc một cấp này không phải để cho gọn: nó khiến chuỗi kế thừa
    /// không bao giờ dài quá một bước, nên không cần dò vòng lặp (A là cha B, B là cha A), không cần đệ quy
    /// khi gom quyền, và cái tên vai trò được cấp thêm vào token luôn là một trong sáu tên mà toàn bộ
    /// [Authorize(Roles = ...)] và chuỗi duyệt khóa học đang dùng.
    /// </summary>
    private async Task<(Role? Role, string? Error)> ResolveParentAsync(int? parentRoleId)
    {
        if (parentRoleId is null) return (null, null);

        var parent = await _db.Role.FirstOrDefaultAsync(x => x.Id == parentRoleId);
        if (parent is null) return (null, "Vai trò cha không tồn tại.");
        if (!parent.IsSystem)
            return (null, $"Chỉ được kế thừa từ vai trò hệ thống. \"{parent.Name}\" là vai trò do người dùng tạo.");

        return (parent, null);
    }

    /// <summary>Xếp mỗi vai trò con ngay dưới vai trò cha của nó, giữ nguyên thứ tự vốn có trong từng nhóm.
    ///
    /// Sắp ở server chứ không để giao diện tự lo: màn hình vai trò, ô lọc người dùng và ô chọn vai trò lúc
    /// tạo tài khoản đều đọc chung danh sách này — sắp ở một chỗ thì cả ba nơi hiện cùng một thứ tự.</summary>
    private static List<RoleListDto> SortByHierarchy(List<RoleListDto> list)
    {
        var childListByParent = list.Where(x => x.ParentRoleId.HasValue)
            .GroupBy(x => x.ParentRoleId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<RoleListDto>();
        foreach (var role in list.Where(x => !x.ParentRoleId.HasValue))
        {
            result.Add(role);
            if (childListByParent.TryGetValue(role.Id, out var childList)) result.AddRange(childList);
        }

        // Vai trò con trỏ vào một cha không còn trong danh sách (dữ liệu lỗi) vẫn phải hiện ra — biến mất
        // khỏi màn hình quản trị là cách chắc chắn để không ai sửa được nó.
        result.AddRange(list.Where(x => !result.Contains(x)));
        return result;
    }

    public async Task<Result> Delete(int id)
    {
        var role = await _db.Role.FirstOrDefaultAsync(x => x.Id == id);
        if (role is null) return Result.NotFound(new MessageResponse("Không tìm thấy vai trò."));

        if (role.IsSystem)
            return Result.Invalid(new MessageResponse($"Không thể xóa vai trò hệ thống \"{role.Name}\"."));

        var userCount = await _db.User.CountAsync(x => x.RoleId == id);
        if (userCount > 0)
            return Result.Invalid(new MessageResponse(
                $"Còn {userCount} tài khoản đang giữ vai trò này. Hãy chuyển họ sang vai trò khác trước khi xóa."));

        // Vai trò đang được giao một bước duyệt mà bị xóa thì luồng đó gãy giữa chừng: khóa học dừng ở bước đó
        // sẽ không còn ai duyệt được nữa. Chặn tại đây để báo rõ lý do, thay vì để khóa ngoại ném lỗi 500.
        var workflowStepCount = await _db.ApprovalWorkflowStep.CountAsync(x => x.RoleId == id);
        if (workflowStepCount > 0)
            return Result.Invalid(new MessageResponse(
                $"Vai trò này đang được giao {workflowStepCount} bước trong các luồng cấp duyệt. Hãy gỡ khỏi các luồng đó trước khi xóa."));

        _db.Role.Remove(role);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa vai trò."));
    }
}
