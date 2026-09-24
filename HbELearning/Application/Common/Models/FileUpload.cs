// File: Application/Common/Models/FileUpload.cs

namespace HbELearning.Application.Common.Models;

/// <summary>
/// Một tệp người dùng tải lên.
///
/// Thay cho IFormFile của ASP.NET Core, vốn là kiểu của tầng HTTP: để nó lọt vào cổng IFileService là lý do lớp
/// Application từng phải tham chiếu cả framework web. Kiểu này cố ý có ĐÚNG các thành viên mà mã nghiệp vụ đã
/// dùng trên IFormFile (FileName, Length, ContentType, OpenReadStream, CopyToAsync), cùng tên cùng ý nghĩa — nhờ
/// vậy chuyển nghiệp vụ sang không phải sửa thân hàm. Tầng API đổi IFormFile sang kiểu này (xem
/// API/Extensions/FileUploadExtensions.cs).
/// </summary>
public sealed class FileUpload
{
    private readonly Func<Stream> _openReadStream;

    public FileUpload(string fileName, long length, string contentType, Func<Stream> openReadStream)
    {
        FileName = fileName;
        Length = length;
        ContentType = contentType;
        _openReadStream = openReadStream;
    }

    /// <summary>Tên tệp phía người dùng (chưa làm sạch — đừng dùng thẳng làm đường dẫn lưu).</summary>
    public string FileName { get; }

    /// <summary>Dung lượng tính bằng byte.</summary>
    public long Length { get; }

    public string ContentType { get; }

    /// <summary>Mở luồng đọc nội dung. Mỗi lần gọi mở một luồng mới; nơi gọi chịu trách nhiệm đóng.</summary>
    public Stream OpenReadStream() => _openReadStream();

    /// <summary>Chép toàn bộ nội dung sang luồng đích.</summary>
    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
    {
        await using var source = _openReadStream();
        await source.CopyToAsync(target, cancellationToken);
    }
}
