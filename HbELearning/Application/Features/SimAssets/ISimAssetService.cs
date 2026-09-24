// File: Application/Features/SimAssets/ISimAssetService.cs

namespace HbELearning.Application.Features.SimAssets;

/// <summary>Các ca sử dụng của chức năng SimAsset. Tầng API gọi qua cổng này (xem API/Controllers/SimAssetService.cs).</summary>
public interface ISimAssetService
{
    /// <summary>Danh sách mục trong thư viện (bối cảnh dựng sẵn + tệp tải lên), lọc theo loại và từ khóa.
    ///
    /// Bối cảnh dựng sẵn xếp TRƯỚC vì đó là vốn có sẵn của hệ thống: người soạn bài mới mở thư viện lần đầu
    /// phải thấy ngay mình đã có gì để dùng, trước khi nghĩ tới việc đi tìm model để tải lên.</summary>
    Task<Result> GetList(string? assetType, string? keyword, CancellationToken cancellationToken);

    /// <summary>Tải một tệp bối cảnh lên thư viện dùng chung.</summary>
    Task<Result> Upload(FileUpload file, string name, string assetType, string? description, string? category, CancellationToken cancellationToken);

    Task<Result> Update(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Lưu ảnh xem trước do trình xem 3D ở trình duyệt chụp lại từ chính khung hình đang xem.</summary>
    Task<Result> SaveThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken);

    /// <summary>Sửa tên gọi / mô tả / nhóm phân loại của một bối cảnh dựng sẵn. Không đụng tới SceneKey vì
    /// đó là thứ mọi mô phỏng đang lưu để biết dựng cảnh nào.</summary>
    Task<Result> UpdateBuiltin(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Lưu ảnh xem trước của một bối cảnh dựng sẵn, chụp từ chính cảnh 3D đang mở trong thư viện.</summary>
    Task<Result> SaveBuiltinThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken);

    /// <summary>Xóa một tệp khỏi thư viện. Tệp đang được mô phỏng nào đó dùng thì TỪ CHỐI xóa — xóa đi là
    /// mô phỏng đó chỉ còn một cảnh trống, và lỗi chỉ lộ ra lúc học viên vào học.</summary>
    Task<Result> Delete(int id, CancellationToken cancellationToken);
}
