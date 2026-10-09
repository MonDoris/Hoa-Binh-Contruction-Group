// File: Application/Common/Interfaces/IFileService.cs
// Cong (port) tach ra tu Infrastructure/Files/FileService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IFileService
{
    Task<UploadResultDto> SaveAsync(FileUpload file, string subFolder);
    bool Delete(string relativeUrl);

    /// <summary>Lưu tệp vào kho RIÊNG nằm ngoài wwwroot — không có đường dẫn công khai nào trỏ tới, chỉ đọc lại
    /// được qua <see cref="OpenPrivate"/> (tức là qua một API đã kiểm quyền). Dùng cho giấy tờ cá nhân.
    /// <paramref name="extension"/> do bên gọi chốt sau khi đã kiểm nội dung tệp, không lấy theo tên tệp.
    /// Trả về đường dẫn TƯƠNG ĐỐI trong kho riêng, dạng "subFolder/yyyyMM/guid.ext".</summary>
    Task<string> SavePrivateAsync(FileUpload file, string subFolder, string extension);

    /// <summary>Mở tệp trong kho riêng để đọc; null nếu đường dẫn sai hoặc tệp không còn.</summary>
    Stream? OpenPrivate(string relativePath);

    bool DeletePrivate(string relativePath);
}
