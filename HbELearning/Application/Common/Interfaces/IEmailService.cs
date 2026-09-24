// File: Application/Common/Interfaces/IEmailService.cs
// Cong (port) tach ra tu Infrastructure/Notifications/EmailService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IEmailService
{
    /// <summary>Đã cấu hình đủ Smtp:Username/Password trong appsettings.json chưa.</summary>
    bool IsConfigured { get; }

    /// <summary>Gửi mã OTP tới hộp thư của người dùng. Ném lỗi nếu máy chủ SMTP từ chối gửi.</summary>
    Task SendOtpAsync(string toEmail, string otpCode, CancellationToken ct = default);

    /// <summary>Gửi một thông báo (VD: nhắc nhở học tập) qua Email. Ném lỗi nếu máy chủ SMTP từ chối gửi.</summary>
    Task SendNotificationAsync(string toEmail, string subject, string bodyText, CancellationToken ct = default);
}
