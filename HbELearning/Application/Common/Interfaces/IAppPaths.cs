// File: Application/Common/Interfaces/IAppPaths.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Các thư mục gốc trên đĩa của ứng dụng.
///
/// Thay cho IWebHostEnvironment — kiểu của ASP.NET Core Hosting — mà nghiệp vụ chỉ dùng đúng hai thuộc tính
/// này để tìm tệp tải lên. Cố ý giữ nguyên tên hai thuộc tính để mã nghiệp vụ chuyển sang không phải sửa.
/// </summary>
public interface IAppPaths
{
    /// <summary>Thư mục phục vụ tệp tĩnh (wwwroot). Có thể rỗng nếu thư mục chưa tồn tại lúc khởi động.</summary>
    string WebRootPath { get; }

    /// <summary>Thư mục gốc nội dung của ứng dụng (nơi chứa appsettings.json).</summary>
    string ContentRootPath { get; }
}
