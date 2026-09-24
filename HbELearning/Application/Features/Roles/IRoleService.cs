// File: Application/Features/Roles/IRoleService.cs

namespace HbELearning.Application.Features.Roles;

/// <summary>Các ca sử dụng của chức năng Role. Tầng API gọi qua cổng này (xem API/Controllers/RoleService.cs).</summary>
public interface IRoleService
{
    /// <summary>Danh mục chức năng + thao tác có thể phân quyền. Giao diện dựng bảng tick quyền từ đây.</summary>
    Result GetFunction();

    Task<Result> GetList();

    Task<Result> GetById(int id);

    Task<Result> Create(RoleSaveRequest request);

    Task<Result> Update(int id, RoleSaveRequest request);

    Task<Result> Delete(int id);
}
