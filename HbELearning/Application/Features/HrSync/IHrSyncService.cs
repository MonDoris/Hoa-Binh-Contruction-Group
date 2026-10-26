// File: Application/Features/HrSync/IHrSyncService.cs
namespace HbELearning.Application.Features.HrSync;

public interface IHrSyncService
{
    /// <summary>Đã kết nối phần mềm nhân sự chưa và lượt đồng bộ gần nhất ra sao.</summary>
    Result GetStatus();

    /// <summary>Đồng bộ phòng ban, vị trí, nhân viên từ phần mềm nhân sự. <paramref name="previewOnly"/> = tính thử,
    /// không ghi gì — để Admin xem trước sẽ thêm / sửa / khóa bao nhiêu người.</summary>
    Task<Result> Run(bool previewOnly, CancellationToken ct = default);
}
