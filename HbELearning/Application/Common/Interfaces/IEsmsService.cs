// File: Application/Common/Interfaces/IEsmsService.cs
// Cong (port) tach ra tu Infrastructure/Notifications/EsmsService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IEsmsService
{
    /// <summary>Đã cấu hình đủ ApiKey/SecretKey/Brandname trong appsettings.json chưa.</summary>
    bool IsConfigured { get; }

    /// <summary>Gửi mã OTP tới số điện thoại (định dạng Việt Nam) qua tin nhắn SMS Brandname. Ném lỗi nếu eSMS từ chối gửi.</summary>
    Task SendOtpAsync(string phoneNumber, string otpCode, CancellationToken ct = default);
}
