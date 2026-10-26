// File: Infrastructure/Files/FileService.cs

namespace HbELearning.Infrastructure.Files;

public class FileService : IFileService
{
    private readonly IWebHostEnvironment _env;
    private readonly long _maxSizeByte;
    private readonly HashSet<string> _allowedExtension;

    /// <summary>Trần dung lượng một tệp khi appsettings không khai Upload:MaxFileSizeMb: 2 GB — đủ cho video bài học
    /// 1080p dài. Trần của yêu cầu HTTP ở UploadController phải không thấp hơn con số này.</summary>
    public const int DefaultMaxFileSizeMb = 2048;

    public FileService(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;

        var maxMb = config.GetValue<int?>("Upload:MaxFileSizeMb") ?? DefaultMaxFileSizeMb;
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

    /// <summary>
    /// Xóa một tệp đã tải lên, theo đường dẫn tương đối mà <see cref="SaveAsync"/> đã trả về.
    ///
    /// Đường dẫn này ĐI TỪ NGƯỜI DÙNG (giao diện gửi lên nguyên văn chuỗi url), nên phải quy về đường dẫn
    /// tuyệt đối rồi kiểm tra nó có còn nằm trong wwwroot/uploads hay không TRƯỚC KHI xóa. Không kiểm thì một
    /// chuỗi kiểu "/uploads/avatar/../../../appsettings.json" ghép vào là thành đường dẫn tới tệp cấu hình của
    /// chính máy chủ — bất kỳ ai đăng nhập được cũng xóa sạch được hệ thống.
    /// </summary>
    public bool Delete(string relativeUrl)
    {
        if (ResolveUploadPath(relativeUrl) is not string path) return false;
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Đường dẫn tuyệt đối của một url tải lên, hoặc null nếu url không trỏ vào trong thư mục
    /// wwwroot/uploads — dùng ký tự trèo thư mục ("../"), đường dẫn tuyệt đối, tên ổ đĩa hay đường dẫn UNC.
    ///
    /// Url hợp lệ là thứ <see cref="SaveAsync"/> trả về: luôn bắt đầu bằng "/uploads/". Ghép nó vào wwwroot
    /// rồi mới quy về đường dẫn thật (GetFullPath tự rút gọn mọi "..") và đối chiếu lại với wwwroot/uploads —
    /// nhờ vậy chỉ cần MỘT phép so sánh cho mọi cách viết vòng vèo.
    /// </summary>
    private string? ResolveUploadPath(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return null;

        // Cắt phần truy vấn / neo nếu giao diện lỡ gửi kèm, rồi giải mã %2e%2e cho khỏi lọt bằng đường vòng.
        var value = relativeUrl.Split('?', '#')[0];
        try { value = Uri.UnescapeDataString(value); } catch { return null; }

        if (value.Length == 0 || value.Contains('\0')) return null;

        // Phải là đường dẫn TƯƠNG ĐỐI tính từ wwwroot. Chặn thẳng "C:\...", "\\máy-khác\thư-mục" và "/etc/..."
        // ngay từ đây thay vì trông chờ phép so sánh cuối hàm.
        if (Path.IsPathRooted(value.TrimStart('/'))) return null;

        var uploadRoot = Path.GetFullPath(Path.Combine(GetWebRoot(), "uploads"));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(GetWebRoot(),
                value.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)));
        }
        catch { return null; }

        return fullPath.StartsWith(uploadRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    // ==================== KHO TỆP RIÊNG (ngoài wwwroot) ====================
    // Thư mục App_Data/private cạnh mã chạy, KHÔNG nằm dưới wwwroot nên middleware tệp tĩnh không bao giờ phục vụ
    // nó. Tệp ở đây chỉ ra ngoài qua một API do bên gọi tự kiểm quyền (VD giấy chứng nhận bên ngoài).

    public async Task<string> SavePrivateAsync(FileUpload file, string subFolder, string extension)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("File rỗng.");
        if (file.Length > _maxSizeByte)
            throw new ArgumentException($"File vượt quá giới hạn {_maxSizeByte / 1024 / 1024} MB.");
        // Thư mục và đuôi tệp đều đi thẳng vào đường dẫn đĩa: chỉ nhận chữ, số, gạch ngang.
        if (!System.Text.RegularExpressions.Regex.IsMatch(subFolder, "^[a-z0-9-]+$"))
            throw new ArgumentException("Thư mục lưu trữ không hợp lệ.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(extension, @"^\.[a-z0-9]{1,8}$"))
            throw new ArgumentException("Đuôi tệp không hợp lệ.");

        var month = DateTime.Now.ToString("yyyyMM");
        var folder = Path.Combine(GetPrivateRoot(), subFolder, month);
        Directory.CreateDirectory(folder);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        await using (var stream = new FileStream(Path.Combine(folder, storedName), FileMode.CreateNew))
            await file.CopyToAsync(stream);

        return $"{subFolder}/{month}/{storedName}";
    }

    public Stream? OpenPrivate(string relativePath)
    {
        if (ResolvePrivatePath(relativePath) is not string path || !File.Exists(path)) return null;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public bool DeletePrivate(string relativePath)
    {
        if (ResolvePrivatePath(relativePath) is not string path || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Đường dẫn tuyệt đối của một tệp trong kho riêng, hoặc null nếu đường dẫn thoát ra ngoài kho —
    /// cùng một phép kiểm như <see cref="ResolveUploadPath"/>: quy về đường dẫn thật rồi đối chiếu tiền tố.</summary>
    private string? ResolvePrivatePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\0')) return null;
        if (Path.IsPathRooted(relativePath.TrimStart('/'))) return null;

        var root = Path.GetFullPath(GetPrivateRoot());
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root,
                relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)));
        }
        catch { return null; }

        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private string GetPrivateRoot()
    {
        var root = Path.Combine(_env.ContentRootPath, "App_Data", "private");
        Directory.CreateDirectory(root);
        return root;
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
