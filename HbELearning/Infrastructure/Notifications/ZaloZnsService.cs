// File: Infrastructure/Notifications/ZaloZnsService.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Infrastructure.Notifications;

/// <summary>
/// Gửi mã OTP qua Zalo Notification Service (ZNS) của Zalo Official Account.
/// Yêu cầu cấu hình mục "ZaloZns" trong appsettings.json:
///   - AppId, SecretKey: lấy từ Zalo Developers (developers.zalo.me) của ứng dụng gắn với OA.
///   - OtpTemplateId: ID mẫu tin ZNS loại OTP đã được Zalo duyệt.
///   - OtpTemplateParamName: tên tham số OTP trong mẫu (đặt đúng như lúc đăng ký mẫu, mặc định "otp").
///   - InitialAccessToken/InitialRefreshToken: cặp token lấy 1 lần từ luồng OAuth cấp quyền cho OA
///     (oauth.zaloapp.com/v4/oa/permission) — chỉ dùng để "mồi" lần đầu, sau đó access_token được
///     service tự làm mới và lưu vào bảng HBE_ZALO_OAUTH_TOKEN (refresh_token của Zalo dùng 1 lần rồi đổi mới).
/// </summary>
public class ZaloZnsService : IZaloZnsService
{
    private const string OAuthTokenUrl = "https://oauth.zaloapp.com/v4/oa/access_token";
    private const string SendMessageUrl = "https://business.openapi.zalo.me/message/template";

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly ILogger<ZaloZnsService> _logger;

    private readonly string? _appId;
    private readonly string? _secretKey;
    private readonly string? _templateId;
    private readonly string _templateParamName;
    private readonly string? _initialAccessToken;
    private readonly string? _initialRefreshToken;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_secretKey) && !string.IsNullOrWhiteSpace(_templateId);

    public ZaloZnsService(HttpClient http, AppDbContext db, IConfiguration config, ILogger<ZaloZnsService> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;

        var section = config.GetSection("ZaloZns");
        _appId = section["AppId"];
        _secretKey = section["SecretKey"];
        _templateId = section["OtpTemplateId"];
        _templateParamName = section["OtpTemplateParamName"] is { Length: > 0 } p ? p : "otp";
        _initialAccessToken = section["InitialAccessToken"];
        _initialRefreshToken = section["InitialRefreshToken"];
    }

    public async Task SendOtpAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình Zalo ZNS (AppId/SecretKey/OtpTemplateId) trong appsettings.json.");

        var normalizedPhone = PhoneHelper.NormalizeVietnamPhone(phoneNumber)
            ?? throw new InvalidOperationException("Số điện thoại không hợp lệ để gửi Zalo ZNS.");

        var accessToken = await GetValidAccessTokenAsync(ct);

        var body = new JsonObject
        {
            ["phone"] = normalizedPhone,
            ["template_id"] = _templateId,
            ["template_data"] = new JsonObject { [_templateParamName] = otpCode },
            ["tracking_id"] = Guid.NewGuid().ToString("N"),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, SendMessageUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("access_token", accessToken);

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(responseBody);
        var errorCode = doc.RootElement.TryGetProperty("error", out var e) ? e.GetInt32() : -1;
        if (errorCode != 0)
        {
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : responseBody;
            _logger.LogWarning("Zalo ZNS gửi OTP thất bại ({Error}): {Message}", errorCode, message);
            throw new InvalidOperationException($"Gửi mã OTP qua Zalo thất bại: {message}");
        }
    }

    /// <summary>Trả về access_token còn hiệu lực, tự làm mới (và lưu lại DB) nếu đã hết hạn hoặc sắp hết hạn.</summary>
    private async Task<string> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var stored = await _db.ZaloOAuthToken.FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (stored is null)
        {
            if (string.IsNullOrWhiteSpace(_initialAccessToken) || string.IsNullOrWhiteSpace(_initialRefreshToken))
                throw new InvalidOperationException(
                    "Chưa có access_token Zalo OA nào. Vui lòng thực hiện cấp quyền OAuth lần đầu cho Official Account " +
                    "(oauth.zaloapp.com/v4/oa/permission) rồi điền InitialAccessToken/InitialRefreshToken vào appsettings.json.");

            stored = new ZaloOAuthToken
            {
                Id = 1,
                AccessToken = _initialAccessToken,
                RefreshToken = _initialRefreshToken,
                AccessTokenExpiresAt = DateTime.Now, // buộc làm mới ngay lượt gọi đầu tiên
            };
            _db.ZaloOAuthToken.Add(stored);
            await _db.SaveChangesAsync(ct);
        }

        if (stored.AccessTokenExpiresAt > DateTime.Now.AddMinutes(1))
            return stored.AccessToken;

        return await RefreshAccessTokenAsync(stored, ct);
    }

    private async Task<string> RefreshAccessTokenAsync(ZaloOAuthToken stored, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OAuthTokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["app_id"] = _appId!,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = stored.RefreshToken,
            }),
        };
        request.Headers.Add("secret_key", _secretKey);

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl))
        {
            _logger.LogError("Làm mới access_token Zalo OA thất bại: {Body}", responseBody);
            throw new InvalidOperationException("Không thể làm mới access_token Zalo OA — kiểm tra lại SecretKey/RefreshToken trong appsettings.json.");
        }

        var newAccessToken = tokenEl.GetString()!;
        var newRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString()! : stored.RefreshToken;
        var expiresInSeconds = doc.RootElement.TryGetProperty("expires_in", out var exp)
            ? int.Parse(exp.GetString() ?? "3600")
            : 3600;

        stored.AccessToken = newAccessToken;
        stored.RefreshToken = newRefreshToken;
        stored.AccessTokenExpiresAt = DateTime.Now.AddSeconds(expiresInSeconds);
        stored.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return newAccessToken;
    }
}
