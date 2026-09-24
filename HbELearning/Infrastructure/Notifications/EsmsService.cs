// File: Infrastructure/Notifications/EsmsService.cs
using System.Text.Json;

namespace HbELearning.Infrastructure.Notifications;

/// <summary>
/// Gửi mã OTP qua SMS Brandname của eSMS.vn (rest.esms.vn).
/// Yêu cầu cấu hình mục "Esms" trong appsettings.json:
///   - ApiKey, SecretKey: lấy tại esms.vn (Cấu hình &gt; API).
///   - Brandname: tên thương hiệu SMS đã đăng ký và được eSMS duyệt.
///   - SandBox: "1" để test không tốn phí (SMS không thực sự được gửi), "0" khi chạy thật.
/// </summary>
public class EsmsService : IEsmsService
{
    private const string SendUrl = "https://rest.esms.vn/MainService.svc/json/SendMultipleMessage_V4_get";

    private readonly HttpClient _http;
    private readonly ILogger<EsmsService> _logger;

    private readonly string? _apiKey;
    private readonly string? _secretKey;
    private readonly string? _brandname;
    private readonly string _sandbox;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_secretKey) && !string.IsNullOrWhiteSpace(_brandname);

    public EsmsService(HttpClient http, IConfiguration config, ILogger<EsmsService> logger)
    {
        _http = http;
        _logger = logger;

        var section = config.GetSection("Esms");
        _apiKey = section["ApiKey"];
        _secretKey = section["SecretKey"];
        _brandname = section["Brandname"];
        _sandbox = section["SandBox"] is { Length: > 0 } s ? s : "0";
    }

    public async Task SendOtpAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình eSMS (ApiKey/SecretKey/Brandname) trong appsettings.json.");

        var normalizedPhone = PhoneHelper.NormalizeVietnamPhone(phoneNumber)
            ?? throw new InvalidOperationException("Số điện thoại không hợp lệ để gửi SMS.");

        // Nội dung không dấu để tính là SMS thường (IsUnicode=0), tránh bị tách nhiều tin nhắn / tính phí cao hơn.
        var content = $"Ma xac thuc (OTP) cua ban la: {otpCode}. Ma co hieu luc trong 5 phut, vui long khong chia se cho bat ky ai.";

        var queryParamMap = new Dictionary<string, string>
        {
            ["Phone"] = normalizedPhone,
            ["Content"] = content,
            ["ApiKey"] = _apiKey!,
            ["SecretKey"] = _secretKey!,
            ["Brandname"] = _brandname!,
            ["SmsType"] = "2",
            ["IsUnicode"] = "0",
            ["Sandbox"] = _sandbox,
        };
        var queryString = string.Join("&", queryParamMap.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

        using var response = await _http.GetAsync($"{SendUrl}?{queryString}", ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(responseBody);
        var codeResult = doc.RootElement.TryGetProperty("CodeResult", out var c) ? c.GetString() : null;
        if (codeResult != "100")
        {
            var errorMessage = doc.RootElement.TryGetProperty("ErrorMessage", out var m) ? m.GetString() : responseBody;
            _logger.LogWarning("eSMS gửi OTP thất bại ({Code}): {Message}", codeResult, errorMessage);
            throw new InvalidOperationException($"Gửi mã OTP qua SMS thất bại: {errorMessage}");
        }
    }
}
