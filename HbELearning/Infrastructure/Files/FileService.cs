// File: Infrastructure/Files/FileService.cs

namespace HbELearning.Infrastructure.Files;

public class FileService : IFileService
{
    private readonly IWebHostEnvironment _env;
    private readonly long _maxSizeByte;
    private readonly HashSet<string> _allowedExtension;

    public FileService(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;

        var maxMb = config.GetValue<int?>("Upload:MaxFileSizeMb") ?? 200;
        _maxSizeByte = maxMb * 1024L * 1024L;

        var allowed = config["Upload:AllowedExtension"] ?? ".pdf,.png,.jpg,.mp4";
        _allowedExtension = allowed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();
    }

    public async Task<UploadResultDto> SaveAsync(FileUpload file, string subFolder)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("File rỗng.");

        if (file.Length > _maxSizeByte)
            throw new ArgumentException($"File vượt quá giới hạn {_maxSizeByte / 1024 / 1024} MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtension.Contains(extension))
            throw new ArgumentException($"Định dạng '{extension}' không được phép.");

        // wwwroot/uploads/<subFolder>/<yyyyMM>/<guid><ext>
        var folder = Path.Combine(GetWebRoot(), "uploads", subFolder, DateTime.Now.ToString("yyyyMM"));
        Directory.CreateDirectory(folder);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, storedName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(stream);

        var url = $"/uploads/{(string.IsNullOrEmpty(subFolder) ? "" : subFolder + "/")}{DateTime.Now:yyyyMM}/{storedName}";
        return new UploadResultDto(file.FileName, url, file.Length, file.ContentType);
    }

    public bool Delete(string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return false;

        var path = Path.Combine(GetWebRoot(), relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    private string GetWebRoot()
    {
        var root = _env.WebRootPath;
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(_env.ContentRootPath, "wwwroot");
            Directory.CreateDirectory(root);
        }
        return root;
    }
}
