// File: Application/Features/Departments/IDepartmentService.cs

namespace HbELearning.Application.Features.Departments;

/// <summary>Các ca sử dụng của chức năng Department. Tầng API gọi qua cổng này (xem API/Controllers/DepartmentService.cs).</summary>
public interface IDepartmentService
{
    Task<Result> GetList(bool? isActive);

    Task<Result> GetById(int id);

    Task<Result> Create(DepartmentRequest request);

    Task<Result> Update(int id, DepartmentRequest request);

    Task<Result> Delete(int id);

    /// <summary>Tải file Excel mẫu để nhập phòng ban hàng loạt.</summary>
    Result DownloadImportTemplate();

    /// <summary>Nhập phòng ban hàng loạt từ file Excel. Dòng nào sai thì bỏ qua dòng đó và báo lỗi kèm số dòng,
    /// các dòng còn lại vẫn được nhập.</summary>
    Task<Result> Import(FileUpload file);
}
