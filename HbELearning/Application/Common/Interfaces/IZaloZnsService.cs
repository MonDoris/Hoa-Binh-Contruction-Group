// File: Application/Common/Interfaces/IZaloZnsService.cs
// Cong (port) tach ra tu Infrastructure/Notifications/ZaloZnsService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IZaloZnsService
{
    /// <summary>Đã cấu hình đủ AppId/SecretKey/OtpTemplateId trong appsettings.json chưa.</summary>
    bool IsConfigured { get; }

    /// <summary>Gửi mã OTP tới số điện thoại (định dạng Việt Nam, VD "0912345678" hoặc "84912345678") qua mẫu ZNS đã đăng ký. Ném lỗi nếu Zalo từ chối gửi.</summary>
    Task SendOtpAsync(string phoneNumber, string otpCode, CancellationToken ct = default);
}
