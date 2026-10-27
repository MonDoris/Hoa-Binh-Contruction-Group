// File: Application/Features/Uploads/UploadService.cs

namespace HbELearning.Application.Features.Uploads;

public class UploadService : IUploadService
{
    private readonly IFileService _fileService;

    /// <summary>Các thư mục lưu trữ hợp lệ. Danh sách đóng vì subFolder đi thẳng vào đường dẫn đĩa:
    /// để tự do thì một giá trị kiểu "../../Controllers" ghi được file ra ngoài wwwroot.</summary>
    private static readonly string[] SubFolderList =
        { "lesson", "course", "avatar", "cover", "ai-document", "classroom-recording", "qr", "scenario", "sim-library", "shop" };

    /// <summary>Thư mục ảnh cá nhân — ai đăng nhập cũng tự tải lên được, các thư mục còn lại chỉ người quản lý.</summary>
    private static readonly string[] PersonalFolderList = { "avatar", "cover" };
    private readonly ICurrentUser _currentUser;

    public UploadService(IFileService fileService, ICurrentUser currentUser)
    {
        _fileService = fileService;
        _currentUser = currentUser;
    }

    public async Task<Result> Upload(FileUpload file, string subFolder = "lesson")
    {
        if (!SubFolderList.Contains(subFolder))
            return Result.Invalid(new MessageResponse("Thư mục lưu trữ không hợp lệ."));
        if (!PersonalFolderList.Contains(subFolder) && !_currentUser.IsManager) return Result.Forbidden();

        try
        {
            var result = await _fileService.SaveAsync(file, subFolder);
            return Result.Success(result);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(new MessageResponse(ex.Message));
        }
    }

    public Result Delete(string url)
    {
        // So theo TIỀN TỐ chứ không phải "có chứa": với Contains thì một chuỗi như
        // "/uploads/lesson/../../appsettings.json#/uploads/avatar/" cũng lọt qua cửa này.
        url = (url ?? string.Empty).Trim();
        var isAvatar = url.StartsWith("/uploads/avatar/", StringComparison.OrdinalIgnoreCase);
        if (!isAvatar && !_currentUser.IsManager) return Result.Forbidden();

        var deleted = _fileService.Delete(url);
        return deleted
            ? Result.Success(new MessageResponse("Đã xóa file."))
            : Result.NotFound(new MessageResponse("Không tìm thấy file."));
    }
}
