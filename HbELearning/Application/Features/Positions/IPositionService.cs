// File: Application/Features/Positions/IPositionService.cs

namespace HbELearning.Application.Features.Positions;

/// <summary>Các ca sử dụng của chức năng Position. Tầng API gọi qua cổng này (xem API/Controllers/PositionService.cs).</summary>
public interface IPositionService
{
    Task<Result> GetList(int? departmentId, bool? isActive);

    Task<Result> GetById(int id);

    Task<Result> Create(PositionRequest request);

    Task<Result> Update(int id, PositionRequest request);

    Task<Result> Delete(int id);

    /// <summary>Tải file Excel mẫu để nhập vị trí công việc hàng loạt.</summary>
    Result DownloadImportTemplate();

    /// <summary>Nhập vị trí công việc hàng loạt từ file Excel. Dòng nào sai thì bỏ qua dòng đó và báo lỗi kèm số dòng,
    /// các dòng còn lại vẫn được nhập.</summary>
    Task<Result> Import(FileUpload file);
}
