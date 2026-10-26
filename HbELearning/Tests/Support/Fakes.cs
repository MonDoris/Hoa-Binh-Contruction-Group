using HbELearning.Application.Common.Interfaces;
using HbELearning.Application.Common.Models;
using HbELearning.Application.DTOs;

namespace HbELearning.Tests.Support;

/// <summary>Đồng hồ do bài test điều khiển — đặt giờ rồi tiến thêm bao nhiêu tùy ý. Giờ địa phương = UTC cho dễ tính.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now += by;
    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}

/// <summary>Người dùng hiện tại do bài test đặt; chỉ các cờ mà phòng học online đọc mới có ý nghĩa.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public string? FullName { get; set; }
    public int? DepartmentId => null;
    public int? PositionId => null;
    public bool IsAdmin { get; set; }
    public bool IsTrainer => false;
    public bool IsInstructor => false;
    public bool IsHeadOfDepartment => false;
    public bool IsDirector => false;
    public bool IsManager => IsAdmin;
    public bool IsContentViewer => IsAdmin;
    public bool IsInRole(string roleName) => false;
    public string? FindClaim(string claimType) => null;
}

public sealed class TestAppPaths(string webRoot) : IAppPaths
{
    public string WebRootPath { get; } = webRoot;
    public string ContentRootPath { get; } = webRoot;
}

/// <summary>Ghi nhận các lần vá thời lượng thay vì thật sự đọc/ghi tệp WebM.</summary>
public sealed class FakeMediaFileProcessor : IMediaFileProcessor
{
    public List<(string FilePath, double DurationMs)> Patches { get; } = new();

    public bool PatchWebmDuration(string filePath, double durationMs)
    {
        Patches.Add((filePath, durationMs));
        return true;
    }
}

public sealed class NullFileService : IFileService
{
    public Task<UploadResultDto> SaveAsync(FileUpload file, string subFolder) => throw new NotSupportedException();
    public bool Delete(string relativeUrl) => true;
    public Task<string> SavePrivateAsync(FileUpload file, string subFolder, string extension) => throw new NotSupportedException();
    public Stream? OpenPrivate(string relativePath) => null;
    public bool DeletePrivate(string relativePath) => true;
}

/// <summary>Mã QR chứng chỉ giả — chỉ để DI dựng được chuỗi LearningService → CertificateIssuer khi test các dịch vụ
/// kéo theo nó (VD QuizService); các bài test này không phát hành chứng chỉ.</summary>
public sealed class FakeQrCodeGenerator : ICertificateQrCodeGenerator
{
    public string Generate(string verificationCode) => "data:image/png;base64,";
}
