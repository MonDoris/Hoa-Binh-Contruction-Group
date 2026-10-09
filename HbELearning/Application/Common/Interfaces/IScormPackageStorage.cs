// File: Application/Common/Interfaces/IScormPackageStorage.cs
using System.IO.Compression;

namespace HbELearning.Application.Common.Interfaces;

/// <summary>Kết quả giải nén một gói SCORM vào kho.</summary>
public sealed record ScormExtractResult(string StorageKey, int FileCount, long ExtractedSize);

/// <summary>
/// Kho tệp của các gói SCORM: <c>App_Data/scorm/{StorageKey}/</c>, phục vụ ra trình duyệt ở
/// <c>/scorm-content/{StorageKey}/...</c>.
///
/// Tách thành cổng riêng vì đây là chỗ DUY NHẤT hệ thống giải nén tệp người dùng tải lên ra đĩa — mọi phép phòng thủ
/// (đường dẫn trèo thư mục "zip slip", bom nén, số tệp, đuôi tệp) nằm gọn ở một hiện thực để rà soát.
/// </summary>
public interface IScormPackageStorage
{
    /// <summary>Giải nén tệp .zip vào một thư mục mới. Ném <see cref="InvalidDataException"/> với thông điệp tiếng
    /// Việt đọc được nếu tệp không phải zip, có đường dẫn nguy hiểm, quá lớn hoặc thiếu imsmanifest.xml.
    /// Gói bọc trong đúng một thư mục con (kiểu "MyCourse/imsmanifest.xml") được kéo lên gốc.</summary>
    Task<ScormExtractResult> ExtractAsync(Stream zipStream, CancellationToken cancellationToken = default);

    /// <summary>Đọc nội dung chữ của một tệp trong gói; null nếu không có.</summary>
    string? ReadText(string storageKey, string relativePath);

    /// <summary>Tệp có tồn tại trong gói không (đường dẫn tương đối, phân biệt hoa thường như trên máy chủ web).</summary>
    bool FileExists(string storageKey, string relativePath);

    /// <summary>Xóa cả thư mục của gói. Không lỗi nếu không còn.</summary>
    void Delete(string storageKey);

    /// <summary>Chép toàn bộ tệp của gói vào một tệp zip đang ghi, dưới tiền tố <paramref name="prefix"/> ("" = gốc).
    /// <paramref name="includeManifest"/> = false bỏ imsmanifest.xml — khi gói được NHÚNG vào một gói khác, manifest
    /// lồng bên trong là thừa và vài LMS kiểm chặt còn từ chối.</summary>
    Task AddPackageToZipAsync(string storageKey, ZipArchive archive, string prefix, bool includeManifest = true,
        CancellationToken cancellationToken = default);

    /// <summary>Mở một tệp đã tải lên của hệ thống (url dạng "/uploads/...") để đóng vào gói xuất; null nếu url
    /// không trỏ vào wwwroot/uploads hoặc tệp không còn.</summary>
    Stream? OpenUpload(string uploadUrl);
}
