// File: Infrastructure/Files/ScormPackageStorage.cs
using System.IO.Compression;

namespace HbELearning.Infrastructure.Files;

/// <summary>Hiện thực <see cref="IScormPackageStorage"/> trên đĩa: <c>{ContentRoot}/App_Data/scorm/{key}/</c>.</summary>
public sealed class ScormPackageStorage : IScormPackageStorage
{
    /// <summary>Thư mục gốc của kho, tương đối với ContentRoot — API/Extensions/WebApplicationExtensions phục vụ
    /// đúng thư mục này ở /scorm-content.</summary>
    public const string RelativeRoot = "App_Data/scorm";

    private const string ManifestName = "imsmanifest.xml";

    private readonly IAppPaths _paths;
    private readonly long _maxExtractedByte;
    private readonly int _maxEntryCount;

    public ScormPackageStorage(IAppPaths paths, IConfiguration configuration)
    {
        _paths = paths;
        // Trần cho bom nén: đếm theo SỐ BYTE THẬT ghi ra đĩa, không tin con số kích thước khai trong zip — kẻ
        // xấu sửa được con số đó, còn dòng byte giải nén ra thì không.
        _maxExtractedByte = (configuration.GetValue<long?>("Scorm:MaxExtractedMb") ?? 2048) * 1024L * 1024L;
        _maxEntryCount = configuration.GetValue<int?>("Scorm:MaxFileCount") ?? 20_000;
    }

    public static string ResolveRoot(string contentRootPath)
        => Path.Combine(contentRootPath, "App_Data", "scorm");

    private string Root
    {
        get
        {
            var root = ResolveRoot(_paths.ContentRootPath);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public async Task<ScormExtractResult> ExtractAsync(Stream zipStream, CancellationToken cancellationToken = default)
    {
        // ZipArchive cần đọc ngẫu nhiên. Luồng tải lên của ASP.NET đã đệm ra đĩa nên thường tua được; không tua
        // được thì chép tạm ra tệp, KHÔNG chép vào bộ nhớ — gói SCORM có video nặng hàng trăm MB.
        string? tempPath = null;
        Stream source = zipStream;
        if (!zipStream.CanSeek)
        {
            tempPath = Path.Combine(Path.GetTempPath(), $"hbe-scorm-{Guid.NewGuid():N}.zip");
            source = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            await zipStream.CopyToAsync(source, cancellationToken);
            source.Position = 0;
        }

        var key = Guid.NewGuid().ToString("N");
        var target = Path.Combine(Root, key);
        var targetFull = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

        try
        {
            ZipArchive archive;
            // Tệp quá ngắn để có phần "central directory" thì ZipArchive ném ArgumentOutOfRange (tua ra trước đầu
            // luồng) chứ không ném InvalidData — gom hết về một thông báo người dùng đọc được.
            try { archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
            { throw new InvalidDataException("Tệp tải lên không phải tệp .zip hợp lệ."); }

            using (archive)
            {
                var entryList = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name)) // bỏ dòng thư mục
                    .Select(e => (Entry: e, Path: NormalizeEntryName(e.FullName)))
                    .Where(x => x.Path is not null && !IsJunk(x.Path))
                    .ToList();

                if (entryList.Count == 0) throw new InvalidDataException("Tệp .zip rỗng.");
                if (entryList.Count > _maxEntryCount)
                    throw new InvalidDataException($"Gói có quá nhiều tệp (tối đa {_maxEntryCount:N0}).");

                // imsmanifest.xml phải ở gốc. Nhiều người nén cả thư mục khóa học chứ không nén nội dung bên trong
                // — nhận luôn trường hợp đó: mọi tệp nằm chung một thư mục con có chứa manifest thì bóc lớp vỏ ấy.
                var prefix = "";
                if (!entryList.Any(x => x.Path!.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)))
                {
                    var topFolderSet = entryList.Select(x => x.Path!.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();
                    if (topFolderSet.Count == 1
                        && entryList.Any(x => x.Path!.Equals($"{topFolderSet[0]}/{ManifestName}", StringComparison.OrdinalIgnoreCase)))
                        prefix = topFolderSet[0] + "/";
                    else
                        throw new InvalidDataException("Không thấy imsmanifest.xml ở gốc gói — đây không phải gói SCORM.");
                }

                Directory.CreateDirectory(target);
                long written = 0;
                var count = 0;
                var buffer = new byte[81920];

                foreach (var (entry, path) in entryList)
                {
                    var relative = path!.Substring(prefix.Length);
                    if (relative.Length == 0) continue;
                    if (relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) relative = ManifestName;

                    var destination = Path.GetFullPath(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)));
                    // Zip slip: sau khi quy về đường dẫn thật mà thoát khỏi thư mục gói là từ chối CẢ gói.
                    if (!destination.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Gói chứa đường dẫn không an toàn: {entry.FullName}");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        written += read;
                        if (written > _maxExtractedByte)
                            throw new InvalidDataException($"Gói giải nén ra vượt quá {_maxExtractedByte / 1024 / 1024:N0} MB.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    count++;
                }

                return new ScormExtractResult(key, count, written);
            }
        }
        catch
        {
            TryDeleteFolder(target);
            throw;
        }
        finally
        {
            if (tempPath is not null) await source.DisposeAsync();
        }
    }

    public string? ReadText(string storageKey, string relativePath)
    {
        var path = Resolve(storageKey, relativePath);
        return path is not null && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool FileExists(string storageKey, string relativePath)
    {
        var path = Resolve(storageKey, relativePath);
        return path is not null && File.Exists(path);
    }

    public void Delete(string storageKey)
    {
        if (!IsValidKey(storageKey)) return;
        TryDeleteFolder(Path.Combine(Root, storageKey));
    }

    public async Task AddPackageToZipAsync(string storageKey, ZipArchive archive, string prefix, bool includeManifest = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(storageKey)) return;
        var folder = Path.Combine(Root, storageKey);
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!includeManifest && relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
            var entry = archive.CreateEntry(prefix + relative, CompressionLevel.Optimal);
            await using var input = File.OpenRead(file);
            await using var output = entry.Open();
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    public Stream? OpenUpload(string uploadUrl)
    {
        if (string.IsNullOrWhiteSpace(uploadUrl)) return null;
        var value = uploadUrl.Split('?', '#')[0];
        try { value = Uri.UnescapeDataString(value); } catch { return null; }
        if (!value.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || value.Contains('\0')) return null;

        var webRoot = string.IsNullOrEmpty(_paths.WebRootPath) ? Path.Combine(_paths.ContentRootPath, "wwwroot") : _paths.WebRootPath;
        var uploadRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads")) + Path.DirectorySeparatorChar;
        string full;
        try { full = Path.GetFullPath(Path.Combine(webRoot, value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))); }
        catch { return null; }

        if (!full.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    // ==================== NỘI BỘ ====================

    private string? Resolve(string storageKey, string relativePath)
    {
        if (!IsValidKey(storageKey) || string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\0')) return null;
        var folder = Path.GetFullPath(Path.Combine(Root, storageKey)) + Path.DirectorySeparatorChar;
        string full;
        try { full = Path.GetFullPath(Path.Combine(folder, relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar))); }
        catch { return null; }
        return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsValidKey(string? key)
        => key is { Length: 32 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Chuẩn hóa tên mục trong zip; null nếu là đường dẫn tuyệt đối hoặc có đoạn "..".</summary>
    private static string? NormalizeEntryName(string fullName)
    {
        var name = fullName.Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.Contains(':') || name.Contains('\0')) return null;
        var segmentList = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentList.Any(s => s is "." or "..")) return null;
        return string.Join('/', segmentList);
    }

    /// <summary>Rác của trình nén trên macOS / Windows, và web.config — tệp cấu hình máy chủ không có chỗ trong nội dung học.</summary>
    private static bool IsJunk(string path)
        => path.StartsWith("__MACOSX/", StringComparison.Ordinal)
           || path.EndsWith("/.DS_Store", StringComparison.Ordinal) || path == ".DS_Store"
           || path.EndsWith("Thumbs.db", StringComparison.OrdinalIgnoreCase)
           || Path.GetFileName(path).Equals("web.config", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch { /* dọn rác không thành thì thôi — không được che mất lỗi gốc */ }
    }
}
