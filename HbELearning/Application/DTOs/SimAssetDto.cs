// File: Application/DTOs/SimAssetDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Một mục trong thư viện bối cảnh 3D. Thư viện có HAI loại mục dùng chung một khuôn dữ liệu này:
///
///  • Tệp tải lên (<see cref="SimAsset"/>): <paramref name="SceneKey"/> null, <paramref name="FileUrl"/>
///    là đường dẫn tệp trong wwwroot.
///  • Bối cảnh dựng sẵn bằng code (<see cref="SimBuiltinScene"/>): <paramref name="SceneKey"/> có giá
///    trị ("construction", "hr-office"...), <paramref name="FileUrl"/> null vì không có tệp nào cả, và
///    <paramref name="AssetType"/> mang giá trị quy ước "builtin".
///
/// Gộp chung một DTO chứ không tách hai danh sách vì người dùng nhìn thư viện là một kho duy nhất: lọc, tìm
/// kiếm và xem trước phải làm được trên cả hai loại mà không phải bấm qua lại giữa hai tab.
/// <paramref name="SceneKey"/> chính là thứ phía giao diện dùng để phân biệt: có key = bối cảnh dựng sẵn.
///
/// <paramref name="UsedByScenarioCount"/> là số mô phỏng đang dùng mục này — hiện ngay trên danh sách để
/// người quản trị biết trước cái gì đang được dùng, thay vì bấm Xóa rồi mới nhận thông báo từ chối.</summary>
public record SimAssetListItemDto(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string AssetType,
    string? FileUrl,
    string? OriginalFileName,
    long FileSizeByte,
    string? ThumbnailUrl,
    string? CreatedByName,
    DateTime CreatedAt,
    int UsedByScenarioCount,
    string? SceneKey = null);

/// <summary>Sửa thông tin mô tả của một mục trong thư viện (không đụng tới tệp đã tải lên, và với bối cảnh
/// dựng sẵn thì cũng không đụng tới mã dựng cảnh — chỉ đổi tên gọi).</summary>
public record SimAssetUpdateRequest(string Name, string? Description, string? Category);

/// <summary>Ảnh xem trước do trình duyệt chụp từ khung hình 3D, gửi lên dạng data URL (image/png hoặc image/jpeg).</summary>
public record SimAssetThumbnailRequest(string DataUrl);
