// File: Infrastructure/Certificates/CertificateQrCodeGenerator.cs
using QRCoder;

namespace HbELearning.Infrastructure.Certificates;

/// <summary>Hiện thực ICertificateQrCodeGenerator bằng QRCoder. Nội dung chép nguyên từ hàm GenerateQrCode từng
/// nằm trong CertificateService, nên ảnh QR và đường dẫn trả về không đổi.</summary>
public sealed class CertificateQrCodeGenerator : ICertificateQrCodeGenerator
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CertificateQrCodeGenerator(IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
    {
        _env = env;
        _httpContextAccessor = httpContextAccessor;
    }

    public string Generate(string verificationCode)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}"
            : "https://localhost:7000";

        var payload = $"{baseUrl}/pages/verify.html?code={verificationCode}";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngBytes = new PngByteQRCode(data).GetGraphic(10);

        var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var folder = Path.Combine(root, "uploads", "qr");
        Directory.CreateDirectory(folder);

        var fileName = $"{verificationCode}.png";
        File.WriteAllBytes(Path.Combine(folder, fileName), pngBytes);

        return $"/uploads/qr/{fileName}";
    }
}
