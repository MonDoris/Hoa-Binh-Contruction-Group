// File: Application/Common/Interfaces/IFileService.cs
// Cong (port) tach ra tu Infrastructure/Files/FileService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IFileService
{
    Task<UploadResultDto> SaveAsync(FileUpload file, string subFolder);
    bool Delete(string relativeUrl);
}
