// File: Infrastructure/Notifications/EmailService.cs
using System.Net;
using System.Net.Mail;

namespace HbELearning.Infrastructure.Notifications;

/// <summary>
/// Gửi mã OTP qua Email bằng SMTP (mặc định trỏ sẵn tới Gmail).
/// Yêu cầu cấu hình mục "Smtp" trong appsettings.json:
///   - Username: địa chỉ Gmail dùng để gửi (VD: hbelearning.system@gmail.com).
///   - Password: KHÔNG phải mật khẩu Gmail thường — phải là "Mật khẩu ứng dụng" (App Password) tạo tại
///     myaccount.google.com/apppasswords (tài khoản Gmail phải bật xác minh 2 bước trước).
///   - Host/Port/EnableSsl: để mặc định (smtp.gmail.com : 587 : true) nếu dùng Gmail; đổi nếu dùng nhà cung cấp SMTP khác.
/// </summary>
public class EmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _username;
    private readonly string? _password;
    private readonly string _fromName;
    private readonly bool _enableSsl;
    private readonly ILogger<EmailService> _logger;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_password);

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _logger = logger;

        var section = config.GetSection("Smtp");
        _host = section["Host"] is { Length: > 0 } h ? h : "smtp.gmail.com";
        _port = int.TryParse(section["Port"], out var p) ? p : 587;
        _username = section["Username"];
        _password = section["Password"];
        _fromName = section["FromName"] is { Length: > 0 } f ? f : "HB E-Learning";
        _enableSsl = !bool.TryParse(section["EnableSsl"], out var ssl) || ssl;
    }

    public async Task SendOtpAsync(string toEmail, string otpCode, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình gửi Email (Smtp:Username/Password) trong appsettings.json.");

        using var client = new SmtpClient(_host, _port)
        {
            Credentials = new NetworkCredential(_username, _password),
            EnableSsl = _enableSsl,
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_username!, _fromName),
            Subject = "Mã xác thực (OTP) đặt lại mật khẩu — HB E-Learning",
            Body = $"""
                Xin chào,

                Mã xác thực (OTP) để đặt lại mật khẩu của bạn là: {otpCode}

                Mã có hiệu lực trong 5 phút. Vui lòng không chia sẻ mã này cho bất kỳ ai, kể cả nhân viên hỗ trợ.

                Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.

                — HB E-Learning
                """,
            IsBodyHtml = false,
        };
        message.To.Add(toEmail);

        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi OTP qua Email thất bại tới {Email}", toEmail);
            throw new InvalidOperationException($"Gửi mã OTP qua Email thất bại: {ex.Message}");
        }
    }

    public async Task SendNotificationAsync(string toEmail, string subject, string bodyText, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình gửi Email (Smtp:Username/Password) trong appsettings.json.");

        using var client = new SmtpClient(_host, _port)
        {
            Credentials = new NetworkCredential(_username, _password),
            EnableSsl = _enableSsl,
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_username!, _fromName),
            Subject = subject,
            Body = $"{bodyText}\n\n— HB E-Learning",
            IsBodyHtml = false,
        };
        message.To.Add(toEmail);

        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi thông báo qua Email thất bại tới {Email}", toEmail);
            throw new InvalidOperationException($"Gửi thông báo qua Email thất bại: {ex.Message}");
        }
    }
}
