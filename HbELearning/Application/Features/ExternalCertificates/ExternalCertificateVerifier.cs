// File: Application/Features/ExternalCertificates/ExternalCertificateVerifier.cs
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.ExternalCertificates;

/// <summary>Kết quả một lượt đối chiếu nền.</summary>
public enum ExternalCertificateVerifyOutcome
{
    /// <summary>AI đã chốt (xác nhận hoặc từ chối).</summary>
    Decided,
    /// <summary>Giấy không còn chờ (đã bị xóa, Admin đã chốt tay trong lúc AI đọc).</summary>
    Skipped,
    /// <summary>AI đọc hỏng lượt này — đã đếm, lần sau thử lại (hoặc đã từ chối hẳn nếu hỏng quá nhiều lần).</summary>
    Failed,
    /// <summary>Máy chủ AI không dùng được — dừng cả lượt, chờ lần quét sau.</summary>
    AiUnavailable,
}

/// <summary>
/// AI đối chiếu chứng nhận bên ngoài: đọc giấy (<see cref="ICertificateReader"/>), so với thông tin khai
/// (<see cref="ExternalCertificateMatcher"/>), rồi chốt — thay cho bước Admin duyệt tay trước đây.
///
/// Hai đường vào:
///   • NGAY lúc gửi: nhân viên vừa chụp / chọn tệp là giao diện gọi quét (<see cref="ReadAsync"/>), kết quả giữ
///     trong bộ nhớ đệm theo người + dấu vân tay tệp. Bấm Gửi đúng tệp đó thì đối chiếu tức thì, khỏi đọc lại.
///   • NỀN: giấy gửi lên mà chưa có kết quả quét (AI tắt lúc chụp...) nằm ở trạng thái chờ, dịch vụ nền gọi
///     <see cref="VerifyPendingAsync"/> — máy chủ AI bật lại là tự vét hết.
/// </summary>
public class ExternalCertificateVerifier
{
    /// <summary>Số lần AI đọc hỏng tối đa cho một giấy rồi từ chối hẳn — đọc mãi không ra thì tờ giấy (hoặc ảnh)
    /// có vấn đề, nhân viên cần gửi bản khác chứ chờ thêm cũng vậy.</summary>
    public const int MaxAiAttempt = 3;

    /// <summary>Kết quả quét giữ bao lâu cho lúc bấm Gửi — điền biểu mẫu hiếm ai mất quá vài phút, hai giờ là dư.</summary>
    private static readonly TimeSpan ScanCacheTtl = TimeSpan.FromHours(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAppDbContext _db;
    private readonly IFileService _fileService;
    private readonly ICertificateReader _reader;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExternalCertificateVerifier> _logger;

    public ExternalCertificateVerifier(IAppDbContext db, IFileService fileService, ICertificateReader reader,
        IMemoryCache cache, ILogger<ExternalCertificateVerifier> logger)
    {
        _db = db;
        _fileService = fileService;
        _reader = reader;
        _cache = cache;
        _logger = logger;
    }

    public string Model => _reader.Model;

    /// <summary>AI đọc giấy, có bộ nhớ đệm theo (người, nội dung tệp): quét lúc chụp rồi bấm Gửi chỉ tốn một lượt đọc.
    ///
    /// Cùng một tệp đang được đọc dở thì chờ chung lượt đó — nhân viên bấm Gửi khi AI chưa đọc xong, dịch vụ nền nhận
    /// đúng giấy ấy và ngồi chờ cùng lượt đọc của hộp thoại, chứ không bắt Ollama (chạy CPU, mỗi lượt cả phút) đọc lại
    /// từ đầu. Lượt đọc bị hủy theo <paramref name="cancellationToken"/> của nơi KHỞI ĐỘNG nó (người dùng đổi ảnh khác
    /// thì ảnh cũ thôi đọc, nhường chỗ cho ảnh mới).</summary>
    /// <exception cref="AiUnavailableException"/>
    /// <exception cref="CertificateReadException"/>
    public async Task<CertificateReading> ReadAsync(int userId, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (TryGetCached(userId, content, out var cached)) return cached;

        var key = CacheKey(userId, content);
        var reader = _reader;
        var cache = _cache;
        var read = InFlightReadDict.GetOrAdd(key, _ => new Lazy<Task<CertificateReading>>(async () =>
        {
            try
            {
                var reading = await reader.ReadAsync(content, contentType, cancellationToken);
                cache.Set(key, reading, new MemoryCacheEntryOptions { SlidingExpiration = ScanCacheTtl });
                return reading;
            }
            finally
            {
                InFlightReadDict.TryRemove(key, out Lazy<Task<CertificateReading>>? _);
            }
        }));
        return await read.Value.WaitAsync(cancellationToken);
    }

    /// <summary>Lượt đọc đang chạy, theo khóa bộ nhớ đệm. Tĩnh vì mỗi request một scope mới, mà lượt đọc phải dùng
    /// chung được giữa request quét của hộp thoại và dịch vụ nền.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<CertificateReading>>> InFlightReadDict = new();

    private static int _interactiveScanCount;

    /// <summary>Có người đang ngồi chờ AI quét giấy trong hộp thoại. Dịch vụ nền thấy vậy thì chưa đọc giấy tiếp theo:
    /// Ollama chạy CPU đọc từng ảnh một, chen việc nền vào là người đang chờ phải đợi thêm vài phút.</summary>
    public static bool IsInteractiveScanActive => Volatile.Read(ref _interactiveScanCount) > 0;

    /// <summary>Đánh dấu một lượt quét của hộp thoại — <c>using</c> quanh lượt đọc.</summary>
    public static IDisposable BeginInteractiveScan()
    {
        Interlocked.Increment(ref _interactiveScanCount);
        return new ScanMark();
    }

    private sealed class ScanMark : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref _interactiveScanCount);
        }
    }

    public bool TryGetCached(int userId, byte[] content, out CertificateReading reading)
    {
        reading = null!;
        if (!_cache.TryGetValue(CacheKey(userId, content), out CertificateReading? cached) || cached is null) return false;
        reading = cached;
        return true;
    }

    /// <summary>Ghi kết luận của AI vào giấy: những gì AI đọc được, rồi xác nhận (lấp số hiệu / ngày hết hạn nhân
    /// viên bỏ trống bằng giá trị trên giấy) hoặc từ chối kèm lý do.</summary>
    public void Apply(ExternalCertificate certificate, CertificateReading reading, CertificateVerdict verdict, DateTime now)
    {
        certificate.AiResultJson = JsonSerializer.Serialize(ToAiResult(reading, verdict.CheckList, now), JsonOptions);
        if (verdict.IsApproved)
        {
            certificate.CertificateNo ??= verdict.CertificateNoFromPaper;
            certificate.ExpiredAt ??= verdict.ExpiredAtFromPaper;
            certificate.ApproveByAi(now);
        }
        else
            certificate.RejectByAi(verdict.RejectReason ?? "Thông tin trên giấy không khớp.", now);
    }

    public ExternalCertificateAiResultDto ToAiResult(CertificateReading reading, IEnumerable<CertificateCheck> checkList, DateTime readAt)
        => new(Model, readAt, reading.IsCertificate, reading.IsReadable, reading.DocumentType,
            reading.HolderName, reading.Title, reading.Issuer, reading.CertificateNo, reading.IssuedAt, reading.ExpiredAt,
            reading.Note, checkList.Select(x => new ExternalCertificateCheckDto(x.Field, x.Ok, x.Found, x.Message)).ToList());

    public static ExternalCertificateAiResultDto? ParseAiResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ExternalCertificateAiResultDto>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Giấy đang chờ AI, cũ trước — giấy đã đọc hỏng quá số lần cho phép thì thôi.</summary>
    public Task<List<int>> GetPendingIdListAsync(CancellationToken cancellationToken)
        => _db.ExternalCertificate.AsNoTracking()
            .Where(x => x.Status == ExternalCertificateStatus.Pending && x.AiAttemptCount < MaxAiAttempt)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Đối chiếu một giấy đang chờ (dịch vụ nền gọi).</summary>
    public async Task<ExternalCertificateVerifyOutcome> VerifyPendingAsync(int id, CancellationToken cancellationToken)
    {
        var snapshot = await _db.ExternalCertificate.AsNoTracking()
            .Where(x => x.Id == id && x.Status == ExternalCertificateStatus.Pending)
            .Select(x => new { x.UserId, x.User.FullName, x.FilePath, x.ContentType })
            .FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null) return ExternalCertificateVerifyOutcome.Skipped;

        byte[]? content = null;
        await using (var stream = _fileService.OpenPrivate(snapshot.FilePath))
        {
            if (stream is not null)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }
        }

        CertificateReading? reading = null;
        string? failure = null;
        if (content is null)
            failure = "Không tìm thấy tệp quét của giấy này trên máy chủ.";
        else
        {
            try
            {
                reading = await ReadAsync(snapshot.UserId, content, snapshot.ContentType, cancellationToken);
            }
            catch (AiUnavailableException ex)
            {
                _logger.LogWarning("Chua doi chieu duoc chung nhan ben ngoai {Id}: {Message}", id, ex.Message);
                return ExternalCertificateVerifyOutcome.AiUnavailable;
            }
            catch (CertificateReadException ex)
            {
                _logger.LogWarning("AI doc hong chung nhan ben ngoai {Id}: {Message}", id, ex.Message);
                failure = ex.Message;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Lượt đọc dùng chung bị hủy bởi nơi khởi động nó (người dùng đổi ảnh trong hộp thoại) — không phải lỗi
                // của tờ giấy, để lượt quét sau đọc lại.
                return ExternalCertificateVerifyOutcome.Skipped;
            }
        }

        // Nạp lại SAU khi AI đọc xong: một lượt đọc mất cả phút, trong lúc đó nhân viên có thể đã xóa giấy hoặc
        // Admin đã chốt tay — chốt đè lên là xóa mất quyết định của người.
        var certificate = await _db.ExternalCertificate.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (certificate is null || !certificate.IsPending) return ExternalCertificateVerifyOutcome.Skipped;

        var now = DateTime.Now;
        if (reading is null)
        {
            certificate.AiAttemptCount++;
            var isFinal = content is null || certificate.AiAttemptCount >= MaxAiAttempt;
            if (isFinal)
            {
                certificate.RejectByAi(content is null
                    ? failure!
                    : $"AI đã thử đọc giấy này {certificate.AiAttemptCount} lần nhưng không ra kết quả. Hãy chụp lại rõ hơn hoặc tải ảnh / bản PDF khác.", now);
                NotifyOwner(certificate);
            }
            await _db.SaveChangesAsync(cancellationToken);
            return isFinal ? ExternalCertificateVerifyOutcome.Decided : ExternalCertificateVerifyOutcome.Failed;
        }

        var claim = new CertificateClaim(certificate.Title, certificate.Issuer, certificate.CertificateNo,
            certificate.IssuedAt, certificate.ExpiredAt);
        Apply(certificate, reading, ExternalCertificateMatcher.Match(reading, snapshot.FullName, claim), now);
        NotifyOwner(certificate);
        await _db.SaveChangesAsync(cancellationToken);
        return ExternalCertificateVerifyOutcome.Decided;
    }

    /// <summary>Báo chủ giấy kết quả AI chốt — họ đã gửi rồi đi làm việc khác, không ngồi chờ ở trang này.</summary>
    private void NotifyOwner(ExternalCertificate certificate)
    {
        var isApproved = certificate.Status == ExternalCertificateStatus.Approved;
        var message = isApproved
            ? $"AI đã đối chiếu \"{certificate.Title}\" với giấy bạn gửi: thông tin khớp, chứng nhận đã vào hồ sơ của bạn."
            : $"\"{certificate.Title}\": {certificate.RejectReason}";
        _db.Notification.Add(new Notification
        {
            UserId = certificate.UserId,
            Type = NotificationType.ExternalCertificate,
            Title = isApproved ? "Chứng nhận bên ngoài đã được xác nhận" : "Chứng nhận bên ngoài không được xác nhận",
            Message = message.Length <= 500 ? message : message[..499] + "…",
        });
    }

    /// <summary>Khóa bộ nhớ đệm: người gửi + SHA-256 nội dung tệp. Có cả người gửi để kết quả quét của người này
    /// không bao giờ đi đối chiếu cho người khác, dù hai người chọn đúng cùng một tệp.</summary>
    private static string CacheKey(int userId, byte[] content)
        => $"ext-cert-read:{userId}:{Convert.ToHexString(SHA256.HashData(content))}";
}
