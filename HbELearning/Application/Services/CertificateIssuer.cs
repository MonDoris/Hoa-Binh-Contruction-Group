// File: Application/Services/CertificateIssuer.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>
/// Cấp chứng nhận khi một lượt học đạt yêu cầu.
///
/// Quy tắc nghiệp vụ nằm ở đây: mỗi lượt học chỉ có MỘT chứng nhận (gọi lại thì trả bản đã cấp), số hiệu đánh
/// theo năm dạng HBE-năm-số thứ tự 6 chữ số, hiệu lực 2 năm. Việc vẽ ảnh QR là hạ tầng, đi qua
/// <see cref="ICertificateQrCodeGenerator"/>.
/// </summary>
public class CertificateIssuer : ICertificateIssuer
{
    private readonly IAppDbContext _db;
    private readonly ICertificateQrCodeGenerator _qrCodeGenerator;

    public CertificateIssuer(IAppDbContext db, ICertificateQrCodeGenerator qrCodeGenerator)
    {
        _db = db;
        _qrCodeGenerator = qrCodeGenerator;
    }

    public async Task<Certificate> IssueAsync(int enrollmentId, decimal finalScore)
    {
        var existing = await _db.Certificate.FirstOrDefaultAsync(x => x.EnrollmentId == enrollmentId);
        if (existing is not null) return existing;

        var year = DateTime.Now.Year;
        var countThisYear = await _db.Certificate.CountAsync(x => x.IssuedAt.Year == year);

        var verificationCode = Guid.NewGuid().ToString("N");
        var certificate = new Certificate
        {
            EnrollmentId = enrollmentId,
            CertificateNo = $"HBE-{year}-{countThisYear + 1:D6}",
            VerificationCode = verificationCode,
            IssuedAt = DateTime.Now,
            ExpiredAt = DateTime.Now.AddYears(2),
            FinalScore = finalScore,
            QrCodeUrl = _qrCodeGenerator.Generate(verificationCode)
        };

        _db.Certificate.Add(certificate);
        await _db.SaveChangesAsync();
        return certificate;
    }
}
