// File: API/Extensions/FileUploadExtensions.cs

namespace HbELearning.API.Extensions;

/// <summary>Đổi tệp tải lên của ASP.NET Core sang kiểu trung lập của lớp Application.</summary>
public static class FileUploadExtensions
{
    public static FileUpload ToFileUpload(this IFormFile file)
        => new(file.FileName, file.Length, file.ContentType, file.OpenReadStream);
}
