using HbELearning.Application.Common.Models;
using HbELearning.Infrastructure.Files;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace HbELearning.Tests.Files;

/// <summary>
/// Trần dung lượng một tệp tải lên (Upload:MaxFileSizeMb). Video bài học 1080p dễ vượt 500 MB, nên mặc định là 2 GB;
/// quá trần thì báo rõ, và cấu hình hạ thấp trần thì phải được tôn trọng.
///
/// Dùng tệp "giả" khai Length rất lớn nhưng nội dung chỉ vài byte: FileService kiểm trần theo Length trước khi chép,
/// nên không cần ghi hàng GB ra đĩa chỉ để thử một phép so sánh.
/// </summary>
public class FileServiceSizeLimitTests : IDisposable
{
    private const long Mb = 1024L * 1024;
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "hbe-files-" + Guid.NewGuid().ToString("N"));

    public FileServiceSizeLimitTests() => Directory.CreateDirectory(_webRoot);

    public void Dispose()
    {
        if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }

    private sealed class TestEnv(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Development";
    }

    private FileService Service(int? maxMb = null)
    {
        var settings = new Dictionary<string, string?> { ["Upload:AllowedExtension"] = ".mp4" };
        if (maxMb is int mb) settings["Upload:MaxFileSizeMb"] = mb.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new FileService(new TestEnv(_webRoot), config);
    }

    private static FileUpload Video(long length) =>
        new("bai-giang.mp4", length, "video/mp4", () => new MemoryStream(new byte[] { 1, 2, 3, 4 }));

    [Fact]
    public async Task By_default_a_700_MB_lesson_video_is_accepted()
    {
        // 738.742.030 byte: đúng cỡ tệp "Tất cả bài giảng về PCCC..." mà người dùng không tải lên được với trần cũ 200 MB.
        var result = await Service().SaveAsync(Video(738_742_030), "lesson");

        Assert.StartsWith("/uploads/lesson/", result.Url);
        Assert.EndsWith(".mp4", result.Url);
        Assert.Equal(738_742_030, result.SizeByte);
    }

    [Fact]
    public async Task By_default_a_file_over_2_GB_is_rejected_with_the_limit_in_the_message()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().SaveAsync(Video(2048 * Mb + 1), "lesson"));

        Assert.Contains("2048 MB", ex.Message);
    }

    [Fact]
    public async Task A_file_of_exactly_the_limit_is_accepted()
    {
        var result = await Service().SaveAsync(Video(2048 * Mb), "lesson");

        Assert.Equal(2048 * Mb, result.SizeByte);
    }

    [Fact]
    public async Task A_lower_limit_in_configuration_is_respected()
    {
        var service = Service(maxMb: 1);

        await service.SaveAsync(Video(1 * Mb), "lesson");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Video(2 * Mb), "lesson"));
        Assert.Contains("1 MB", ex.Message);
    }

    [Fact]
    public void The_default_limit_is_2_GB()
    {
        Assert.Equal(2048, FileService.DefaultMaxFileSizeMb);
    }
}
