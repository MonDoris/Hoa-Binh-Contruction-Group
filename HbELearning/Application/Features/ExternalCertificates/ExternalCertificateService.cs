// File: Application/Features/ExternalCertificates/ExternalCertificateService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.ExternalCertificates;

public class ExternalCertificateService : IExternalCertificateService
{
    /// <summary>Thư mục trong kho tệp riêng (xem IFileService.SavePrivateAsync).</summary>
    private const string StorageFolder = "external-certificate";

    /// <summary>Một tờ giấy chụp bằng điện thoại cỡ 2–5 MB, một bản PDF quét vài trang cỡ vài MB. 15 MB dư cho cả
    /// hai mà vẫn chặn được người lỡ chọn nhầm một tệp video.</summary>
    private const long MaxFileByte = 15L * 1024 * 1024;

    /// <summary>Trần số dòng trả về cho màn hình Admin — ô lọc là cách tìm, không phải cuộn.</summary>
    private const int MaxListRow = 500;

    private readonly IAppDbContext _db;
    private readonly IFileService _fileService;
    private readonly ICurrentUser _currentUser;
    private readonly ExternalCertificateVerifier _verifier;
    private readonly IExternalCertificateVerifyQueue _verifyQueue;

    public ExternalCertificateService(IAppDbContext db, IFileService fileService, ICurrentUser currentUser,
        ExternalCertificateVerifier verifier, IExternalCertificateVerifyQueue verifyQueue)
    {
        _db = db;
        _fileService = fileService;
        _currentUser = currentUser;
        _verifier = verifier;
        _verifyQueue = verifyQueue;
    }

    public async Task<Result> GetMy()
    {
        var userId = _currentUser.UserId;
        return Result.Success(await QueryDto(_db.ExternalCertificate.Where(x => x.UserId == userId)));
    }

    public async Task<Result> GetList(ExternalCertificateStatus? status, int? departmentId, string? keyword)
    {
        var query = _db.ExternalCertificate.AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (departmentId.HasValue) query = query.Where(x => x.User.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLower();
            query = query.Where(x => x.User.FullName.ToLower().Contains(k)
                || x.User.EmployeeCode.ToLower().Contains(k)
                || x.Title.ToLower().Contains(k)
                || x.Issuer.ToLower().Contains(k));
        }
        return Result.Success(await QueryDto(query, MaxListRow));
    }

    public async Task<Result> GetPendingCount()
        => Result.Success(new { count = await _db.ExternalCertificate.CountAsync(x => x.Status == ExternalCertificateStatus.Pending) });

    public async Task<Result> Scan(FileUpload? file, CancellationToken cancellationToken)
    {
        if (_currentUser.IsAdmin) return AdminDenied();

        var (upload, error) = await ReadUploadAsync(file);
        if (upload is null) return error!;

        CertificateReading reading;
        try
        {
            using (ExternalCertificateVerifier.BeginInteractiveScan())
                reading = await _verifier.ReadAsync(_currentUser.UserId, upload.Content, upload.ContentType, cancellationToken);
        }
        catch (AiUnavailableException ex)
        {
            return Result.ServiceUnavailable(new MessageResponse(ex.Message));
        }
        catch (CertificateReadException ex)
        {
            return Result.ExternalServiceFailed(new MessageResponse(ex.Message));
        }

        // Đối chiếu thử với chính những gì đọc được: phần duy nhất có thể lệch là thứ nhân viên KHÔNG sửa được ở
        // biểu mẫu — không phải giấy chứng nhận, sai người, không đọc ra họ tên / tên chứng nhận. Báo ngay lúc
        // này, đừng để họ điền xong cả biểu mẫu rồi mới biết.
        var accountName = await AccountNameAsync();
        var selfCheck = ExternalCertificateMatcher.Match(reading, accountName, new CertificateClaim(
            reading.Title ?? "", reading.Issuer ?? "", reading.CertificateNo, reading.IssuedAt ?? DateTime.Today, reading.ExpiredAt));
        bool? isNameMatched = string.IsNullOrWhiteSpace(reading.HolderName)
            ? null : ExternalCertificateMatcher.NameMatches(reading.HolderName, accountName);

        return Result.Success(new ExternalCertificateScanDto(
            _verifier.ToAiResult(reading, selfCheck.CheckList, DateTime.Now), accountName, isNameMatched,
            selfCheck.IsApproved ? null : selfCheck.RejectReason));
    }

    public async Task<Result> Create(ExternalCertificateRequest request, FileUpload? file)
    {
        // Admin quản lý chứng nhận, không phải người nộp: tự gửi rồi tự xác nhận giấy của mình là hồ sơ năng lực
        // không ai đối chiếu. Giao diện đã ẩn thẻ "Bên ngoài" với Admin — chặn cả ở đây.
        if (_currentUser.IsAdmin) return AdminDenied();

        var title = request.Title?.Trim();
        var issuer = request.Issuer?.Trim();
        if (string.IsNullOrEmpty(title)) return Invalid("Vui lòng nhập tên chứng nhận.");
        if (string.IsNullOrEmpty(issuer)) return Invalid("Vui lòng nhập đơn vị cấp.");
        if (title.Length > 250 || issuer.Length > 250) return Invalid("Tên chứng nhận / đơn vị cấp tối đa 250 ký tự.");
        if (request.IssuedAt is null) return Invalid("Vui lòng nhập ngày cấp.");

        var issuedAt = request.IssuedAt.Value.Date;
        var expiredAt = request.ExpiredAt?.Date;
        if (issuedAt > DateTime.Today) return Invalid("Ngày cấp không được ở tương lai.");
        if (expiredAt.HasValue && expiredAt < issuedAt) return Invalid("Ngày hết hạn phải sau ngày cấp.");

        var certificateNo = string.IsNullOrWhiteSpace(request.CertificateNo) ? null : request.CertificateNo.Trim();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (certificateNo?.Length > 100) return Invalid("Số hiệu tối đa 100 ký tự.");
        if (note?.Length > 1000) return Invalid("Ghi chú tối đa 1000 ký tự.");

        var (upload, error) = await ReadUploadAsync(file);
        if (upload is null) return error!;

        // AI đã quét đúng tệp này lúc nhân viên vừa chụp / chọn → đối chiếu ngay. Lệch thì KHÔNG lưu: biểu mẫu
        // vẫn mở, nhân viên sửa chỗ khai sai hoặc chụp lại — lưu một giấy bị từ chối chỉ bắt họ xóa rồi gửi lại.
        var now = DateTime.Now;
        CertificateReading? reading = null;
        CertificateVerdict? verdict = null;
        if (_verifier.TryGetCached(_currentUser.UserId, upload.Content, out var cached))
        {
            reading = cached;
            verdict = ExternalCertificateMatcher.Match(reading, await AccountNameAsync(),
                new CertificateClaim(title, issuer, certificateNo, issuedAt, expiredAt));
            if (!verdict.IsApproved)
                return Result.Invalid(new ExternalCertificateMismatchResponse(
                    verdict.RejectReason!, _verifier.ToAiResult(reading, verdict.CheckList, now)));
        }

        var filePath = await _fileService.SavePrivateAsync(file!, StorageFolder, upload.Extension);

        var certificate = new ExternalCertificate
        {
            UserId = _currentUser.UserId,
            Title = title,
            Issuer = issuer,
            CertificateNo = certificateNo,
            IssuedAt = issuedAt,
            ExpiredAt = expiredAt,
            Note = note,
            FilePath = filePath,
            FileName = SafeFileName(file!.FileName, upload.Extension),
            ContentType = upload.ContentType,
            FileSize = upload.Content.Length,
        };
        if (verdict is not null) _verifier.Apply(certificate, reading!, verdict, now);
        _db.ExternalCertificate.Add(certificate);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Lưu dòng thất bại thì tệp vừa ghi thành rác không ai trỏ tới — dọn luôn.
            _fileService.DeletePrivate(filePath);
            throw;
        }

        // Chưa có kết quả quét (máy chủ AI tắt lúc chụp...) → AI đọc nền, xong báo qua thông báo.
        if (verdict is null) _verifyQueue.Enqueue(certificate.Id);

        return Result.Created(nameof(GetMy), null, new MessageResponse(verdict is null
            ? "Đã gửi chứng nhận. AI đang đối chiếu giấy — có kết quả sẽ báo cho bạn."
            : "AI đã đối chiếu: thông tin khớp với giấy, chứng nhận đã vào hồ sơ của bạn.", certificate.Id));
    }

    public async Task<Result> Delete(int id)
    {
        var certificate = await _db.ExternalCertificate.FirstOrDefaultAsync(x => x.Id == id);
        if (certificate is null) return NotFound();

        if (!_currentUser.IsAdmin)
        {
            if (certificate.UserId != _currentUser.UserId) return Result.Forbidden();
            // Giấy đã xác nhận là hồ sơ năng lực của công ty, nằm trong danh sách in ra — nhân viên không tự rút
            // lại được; cần gỡ thì nhờ Admin.
            if (certificate.Status == ExternalCertificateStatus.Approved)
                return Invalid("Chứng nhận đã được xác nhận, không tự xóa được. Liên hệ Admin nếu cần gỡ.");
        }

        _db.ExternalCertificate.Remove(certificate);
        await _db.SaveChangesAsync();
        _fileService.DeletePrivate(certificate.FilePath);
        return Result.Success(new MessageResponse("Đã xóa chứng nhận."));
    }

    public async Task<Result> GetFile(int id)
    {
        var certificate = await _db.ExternalCertificate.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (certificate is null) return NotFound();
        if (!_currentUser.IsAdmin && certificate.UserId != _currentUser.UserId) return Result.Forbidden();

        await using var stream = _fileService.OpenPrivate(certificate.FilePath);
        if (stream is null) return Result.NotFound(new MessageResponse("Không tìm thấy tệp quét của chứng nhận này."));

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Result.FileDownload(buffer.ToArray(), certificate.ContentType, certificate.FileName);
    }

    /// <summary>Admin xác nhận tay — giấy AI chưa đọc được, hoặc AI từ chối nhầm.</summary>
    public async Task<Result> Approve(int id)
    {
        var certificate = await _db.ExternalCertificate.FirstOrDefaultAsync(x => x.Id == id);
        if (certificate is null) return NotFound();
        if (certificate.Status == ExternalCertificateStatus.Approved)
            return Result.Conflict(new MessageResponse("Chứng nhận này đã được xác nhận."));

        certificate.Approve(_currentUser.UserId, DateTime.Now);
        NotifyOwner(certificate, "Chứng nhận bên ngoài đã được xác nhận",
            $"Admin đã xác nhận \"{certificate.Title}\" và đưa vào hồ sơ chứng nhận của bạn.");
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xác nhận chứng nhận."));
    }

    /// <summary>Admin từ chối tay, hoặc THU HỒI một giấy AI đã xác nhận (AI không phát hiện được giấy giả).</summary>
    public async Task<Result> Reject(int id, ExternalCertificateRejectRequest request)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length < 3) return Invalid("Vui lòng nhập lý do từ chối.");
        if (reason.Length > 1000) return Invalid("Lý do từ chối tối đa 1000 ký tự.");

        var certificate = await _db.ExternalCertificate.FirstOrDefaultAsync(x => x.Id == id);
        if (certificate is null) return NotFound();
        if (certificate.Status == ExternalCertificateStatus.Rejected)
            return Result.Conflict(new MessageResponse("Chứng nhận này đã bị từ chối."));

        var wasApproved = certificate.Status == ExternalCertificateStatus.Approved;
        certificate.Reject(_currentUser.UserId, reason, DateTime.Now);
        NotifyOwner(certificate,
            wasApproved ? "Chứng nhận bên ngoài bị thu hồi xác nhận" : "Chứng nhận bên ngoài bị từ chối",
            $"\"{certificate.Title}\" {(wasApproved ? "bị Admin thu hồi xác nhận" : "bị từ chối")}: {reason}");
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse(wasApproved ? "Đã thu hồi xác nhận." : "Đã từ chối chứng nhận."));
    }

    // ==================== HỖ TRỢ ====================

    private sealed record UploadContent(byte[] Content, string Extension, string ContentType);

    /// <summary>Đọc tệp gửi lên vào bộ nhớ (tối đa 15 MB) và xác định kiểu tệp — dùng chung cho Quét và Gửi, để
    /// hai bên tính dấu vân tay trên đúng cùng một dãy byte.</summary>
    private static async Task<(UploadContent? Upload, Result? Error)> ReadUploadAsync(FileUpload? file)
    {
        if (file is null || file.Length == 0) return (null, Invalid("Vui lòng chụp hoặc tải lên giấy chứng nhận."));
        if (file.Length > MaxFileByte) return (null, Invalid($"Tệp vượt quá {MaxFileByte / 1024 / 1024} MB."));

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer);
            content = buffer.ToArray();
        }

        // Kiểu tệp xác định từ NỘI DUNG, không tin đuôi tên tệp lẫn Content-Type trình duyệt khai: tệp này sẽ được
        // trả ngược lại cho trình duyệt của Admin, một tệp HTML đổi đuôi thành .pdf mà lọt qua là chạy được mã.
        var kind = DetectFileKind(content);
        if (kind is null) return (null, Invalid("Chỉ nhận tệp PDF hoặc ảnh (JPG, PNG, WEBP)."));
        return (new UploadContent(content, kind.Value.Extension, kind.Value.ContentType), null);
    }

    private async Task<string> AccountNameAsync()
        => await _db.User.Where(x => x.Id == _currentUser.UserId).Select(x => x.FullName).FirstOrDefaultAsync() ?? "";

    private static Result AdminDenied()
        => Result.Denied(new MessageResponse("Admin chỉ quản lý chứng nhận, không tự gửi chứng nhận bên ngoài."));

    private void NotifyOwner(ExternalCertificate certificate, string title, string message)
        => _db.Notification.Add(new Notification
        {
            UserId = certificate.UserId,
            Type = NotificationType.ExternalCertificate,
            Title = title,
            Message = Truncate(message, 500),
        });

    private static async Task<List<ExternalCertificateDto>> QueryDto(IQueryable<ExternalCertificate> query, int? take = null)
    {
        var ordered = query.AsNoTracking().OrderByDescending(x => x.CreatedAt).AsQueryable();
        if (take.HasValue) ordered = ordered.Take(take.Value);

        var raw = await ordered
            .Select(x => new
            {
                x.Id, x.UserId, x.User.FullName, x.User.EmployeeCode,
                DepartmentName = x.User.Department != null ? x.User.Department.Name : null,
                PositionName = x.User.Position != null ? x.User.Position.Name : null,
                x.Title, x.Issuer, x.CertificateNo, x.IssuedAt, x.ExpiredAt, x.Note,
                x.FileName, x.ContentType, x.FileSize, x.Status, x.RejectReason,
                ReviewedByName = x.ReviewedBy != null ? x.ReviewedBy.FullName : null,
                x.ReviewedAt, x.CreatedAt, x.IsAiReviewed, x.AiResultJson
            })
            .ToListAsync();

        // Status.ToString() đổi ở bộ nhớ chứ không trong câu truy vấn: EF không dịch được ToString() của enum
        // ra SQL trên mọi provider. JSON kết quả AI cũng giải ở đây.
        return raw.Select(x => new ExternalCertificateDto(
                x.Id, x.UserId, x.FullName, x.EmployeeCode, x.DepartmentName, x.PositionName,
                x.Title, x.Issuer, x.CertificateNo, x.IssuedAt, x.ExpiredAt, x.Note,
                x.FileName, x.ContentType, x.FileSize,
                x.Status.ToString(), x.RejectReason, x.ReviewedByName, x.ReviewedAt, x.CreatedAt,
                x.IsAiReviewed, ExternalCertificateVerifier.ParseAiResult(x.AiResultJson)))
            .ToList();
    }

    /// <summary>Kiểu tệp theo chữ ký đầu tệp (magic number): PDF, JPEG, PNG, WEBP. Null = không nhận.</summary>
    private static (string Extension, string ContentType)? DetectFileKind(byte[] content)
    {
        if (content.Length < 12) return null;
        bool StartsWith(params byte[] signature) => content.AsSpan(0, signature.Length).SequenceEqual(signature);

        if (StartsWith(0x25, 0x50, 0x44, 0x46, 0x2D)) return (".pdf", "application/pdf");               // %PDF-
        if (StartsWith(0xFF, 0xD8, 0xFF)) return (".jpg", "image/jpeg");
        if (StartsWith(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return (".png", "image/png");
        if (StartsWith(0x52, 0x49, 0x46, 0x46)                                                           // RIFF....WEBP
            && content.AsSpan(8, 4).SequenceEqual(new byte[] { 0x57, 0x45, 0x42, 0x50 })) return (".webp", "image/webp");
        return null;
    }

    /// <summary>Tên tệp để hiển thị / tải về: chỉ giữ phần tên (bỏ đường dẫn người dùng gửi kèm), cắt cho vừa cột,
    /// và đuôi theo kiểu tệp THẬT — "giay.pdf" mà nội dung là ảnh JPEG thì tải về thành "giay.jpg".</summary>
    private static string SafeFileName(string? original, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(original ?? "");
        if (string.IsNullOrWhiteSpace(name)) name = "chung-nhan";
        name = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        if (name.Length == 0) name = "chung-nhan";
        if (name.Length > 200) name = name[..200];
        return name + extension;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static Result Invalid(string message) => Result.Invalid(new MessageResponse(message));
    private static Result NotFound() => Result.NotFound(new MessageResponse("Không tìm thấy chứng nhận."));
}
