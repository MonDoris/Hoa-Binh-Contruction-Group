// File: Infrastructure/Ai/OllamaCertificateReader.cs
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace HbELearning.Infrastructure.Ai;

/// <summary>
/// Đọc giấy chứng nhận bằng model đọc-được-ảnh (VD qwen3-vl) chạy nội bộ qua Ollama — giấy tờ mang họ tên, số
/// định danh của nhân viên nên không gửi ra dịch vụ bên ngoài nào, cùng lý do với phần AI còn lại của hệ thống.
///
/// Ảnh gửi nguyên cho Ollama (model tự thu về cỡ nó đọc được). PDF thì máy chủ không vẽ được trang, nên lấy hai
/// thứ PDF có sẵn: ẢNH QUÉT nhúng trong trang (PDF từ máy scan / ứng dụng scan điện thoại thực chất là một ảnh
/// phủ kín trang) và LỚP CHỮ (PDF xuất từ phần mềm của đơn vị cấp). Có thứ nào gửi thứ đó.
/// </summary>
public class OllamaCertificateReader : ICertificateReader
{
    /// <summary>Chỉ đọc vài trang đầu: chứng nhận hầu như chỉ một trang, trang sau thường là mặt sau / phụ lục.</summary>
    private const int MaxPdfPage = 2;

    /// <summary>Trần chữ lấy từ lớp chữ PDF — đủ cho một tờ chứng nhận, chặn tệp PDF dày bị gửi nhầm.</summary>
    private const int MaxPdfTextCharacter = 3000;

    /// <summary>Ảnh nhỏ hơn thế này trong PDF là logo / con dấu / chữ ký, không phải bản quét cả trang.</summary>
    private const int MinScanImageSide = 400;

    private static readonly string[] DateFormatList =
    [
        "yyyy-MM-dd", "yyyy-M-d", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "yyyy/MM/dd", "yyyy/M/d",
    ];

    /// <summary>Những gì model hay viết thay cho "không có" dù câu lệnh đã dặn để chuỗi rỗng.</summary>
    private static readonly HashSet<string> EmptyWordSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "none", "n/a", "na", "unknown", "không có", "không rõ", "không xác định", "không đọc được", "-", "—",
    };

    private readonly HttpClient _http;
    private readonly ILogger<OllamaCertificateReader> _logger;
    private readonly string _baseUrl;
    private readonly string _keepAlive;
    private readonly int _contextWindow;

    public string Model { get; }

    public OllamaCertificateReader(HttpClient http, IConfiguration config, ILogger<OllamaCertificateReader> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["Ollama:BaseUrl"] is { Length: > 0 } url ? url : "http://localhost:11434").TrimEnd('/');
        Model = config["Ollama:VisionModel"] is { Length: > 0 } model ? model : "qwen3-vl:4b-instruct";
        _keepAlive = config["Ollama:KeepAlive"] is { Length: > 0 } keepAlive ? keepAlive : "60m";
        _contextWindow = config.GetValue<int?>("Ollama:VisionContextToken") ?? 8192;

        // Trên máy chạy CPU một lượt đọc ảnh mất cỡ một phút, cộng thời gian nạp model lần đầu và thời gian xếp
        // hàng sau lượt soạn bài / hỏi đáp đang chạy trên cùng Ollama.
        _http.Timeout = TimeSpan.FromSeconds(config.GetValue<int?>("Ollama:VisionTimeoutSeconds") ?? 600);
    }

    public async Task<CertificateReading> ReadAsync(byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        List<byte[]> imageList;
        var pdfText = "";
        if (contentType == "application/pdf")
        {
            (imageList, pdfText) = ExtractPdf(content);
            if (imageList.Count == 0 && pdfText.Length == 0)
                return new CertificateReading(false, false, null, null, null, null, null, null, null,
                    "Tệp PDF không có lớp chữ lẫn ảnh quét mà hệ thống đọc được — hãy chụp ảnh tờ giấy hoặc tải ảnh lên.");
        }
        else
            imageList = [content];

        var message = new JsonObject { ["role"] = "user", ["content"] = BuildPrompt(pdfText, imageList.Count) };
        if (imageList.Count > 0)
            message["images"] = new JsonArray(imageList.Select(x => (JsonNode)Convert.ToBase64String(x)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["keep_alive"] = _keepAlive,
            ["messages"] = new JsonArray { message },
            // Ép đúng lược đồ JSON: model nhỏ để tự do hay kèm lời dẫn hoặc đổi tên trường.
            ["format"] = BuildSchema(),
            ["options"] = new JsonObject
            {
                // Nhiệt độ 0: việc ở đây là chép lại chữ trên giấy, cùng một tờ giấy phải ra cùng một kết quả.
                ["temperature"] = 0,
                ["num_predict"] = 400,
                ["num_ctx"] = _contextWindow,
            },
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"{_baseUrl}/api/chat",
                new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException($"Không kết nối được máy chủ AI nội bộ ({_baseUrl}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CertificateReadException("AI đọc giấy quá lâu nên đã dừng.", ex);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Ollama tra ve {Status} khi doc chung nhan ({Model}): {Body}", (int)response.StatusCode, Model, responseBody);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new AiUnavailableException($"Máy chủ AI chưa cài model đọc ảnh {Model} (chạy: ollama pull {Model}).");
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    throw new AiUnavailableException("Máy chủ AI nội bộ đang quá tải, thử lại sau.");
                throw new CertificateReadException($"Máy chủ AI báo lỗi ({(int)response.StatusCode}) khi đọc giấy.");
            }

            return ParseReading(responseBody);
        }
    }

    // ==================== CÂU LỆNH ====================

    private static string BuildPrompt(string pdfText, int imageCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine(imageCount > 0
            ? "Ảnh đính kèm là giấy tờ một nhân viên nộp làm \"chứng nhận đào tạo bên ngoài\" (giấy chứng nhận, chứng chỉ, giấy hoàn thành khóa học, bằng...)."
            : "Dưới đây là chữ trong tệp PDF một nhân viên nộp làm \"chứng nhận đào tạo bên ngoài\" (giấy chứng nhận, chứng chỉ, giấy hoàn thành khóa học, bằng...).");
        builder.AppendLine("Đọc CHÍNH XÁC những gì in trên giấy, không đoán, không bịa. Trường nào giấy không ghi hoặc không đọc rõ thì để chuỗi rỗng \"\".");
        builder.AppendLine("- documentType: loại giấy tờ, VD \"Giấy chứng nhận\", \"Chứng chỉ\", \"Certificate of Completion\", \"Căn cước công dân\", \"Hóa đơn\".");
        builder.AppendLine("- holderName: họ tên NGƯỜI ĐƯỢC CẤP giấy (không phải người ký, không phải tên đơn vị). Giữ nguyên dấu tiếng Việt.");
        builder.AppendLine("- title: tên khóa học / chương trình CỤ THỂ người này đã hoàn thành (giấy ghi \"Đã hoàn thành khóa học: Sơ cấp cứu cơ bản\" thì lấy \"Sơ cấp cứu cơ bản\", không lấy dòng tiêu đề chung của loại giấy); giấy không ghi tên khóa thì lấy tên chứng nhận / chứng chỉ.");
        builder.AppendLine("- issuer: tên đơn vị TRỰC TIẾP cấp giấy (trường, trung tâm, công ty, tổ chức) — không ghi kèm cơ quan chủ quản phía trên nó.");
        builder.AppendLine("- certificateNo: số hiệu / mã số / số vào sổ của giấy.");
        builder.AppendLine("- issuedAt: ngày cấp, dạng yyyy-MM-dd. Giấy Việt Nam ghi ngày trước tháng sau: \"30/9/2026\" hay \"ngày 30 tháng 9 năm 2026\" là 2026-09-30.");
        builder.AppendLine("- expiredAt: ngày hết hạn / có giá trị đến, dạng yyyy-MM-dd; giấy không ghi thì \"\".");
        builder.AppendLine("- isCertificate: true nếu là giấy chứng nhận / chứng chỉ / bằng cấp về đào tạo; false nếu là loại giấy khác hoặc không phải giấy tờ.");
        builder.AppendLine("- isReadable: false nếu ảnh mờ, lóa, bị che hoặc cắt mất khiến không đọc được họ tên hay tên chứng nhận.");
        // Mỗi token sinh ra trên CPU tốn cỡ 0,3 giây — xuống dòng, thụt lề của JSON "đẹp" là vài chục token vô ích.
        builder.AppendLine("Trả về JSON gọn trên MỘT dòng, không xuống dòng, không thụt lề.");
        if (pdfText.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Chữ lấy từ lớp văn bản của tệp PDF, mỗi dòng trên giấy là một dòng dưới đây (chính xác từng ký tự, ưu tiên dùng khi có):");
            builder.AppendLine(pdfText);
        }
        return builder.ToString();
    }

    private static JsonObject BuildSchema()
    {
        static JsonObject Text() => new() { ["type"] = "string" };
        static JsonObject Flag() => new() { ["type"] = "boolean" };

        // Thứ tự trường là thứ tự model sinh ra: đọc hết các ô trên giấy TRƯỚC rồi mới phán có phải giấy chứng
        // nhận không — phán trước thì model hay "chốt" sai rồi bỏ trống mọi ô sau cho khớp lời đã phán.
        // Không xin thêm câu nhận xét tự do: đo thử thì model nhỏ chép lại một dòng trên giấy vào đó, tốn cả chục
        // giây sinh chữ mà không dùng được.
        var propertyList = new (string Name, JsonObject Type)[]
        {
            ("documentType", Text()), ("holderName", Text()), ("title", Text()), ("issuer", Text()),
            ("certificateNo", Text()), ("issuedAt", Text()), ("expiredAt", Text()),
            ("isCertificate", Flag()), ("isReadable", Flag()),
        };

        var properties = new JsonObject();
        foreach (var (name, type) in propertyList) properties[name] = type;
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(propertyList.Select(x => (JsonNode)x.Name).ToArray()),
        };
    }

    // ==================== ĐỌC KẾT QUẢ ====================

    private CertificateReading ParseReading(string responseBody)
    {
        string? text;
        try
        {
            using var envelope = JsonDocument.Parse(responseBody);
            text = envelope.RootElement.TryGetProperty("message", out var message)
                   && message.TryGetProperty("content", out var content) ? content.GetString() : null;
        }
        catch (JsonException ex)
        {
            throw new CertificateReadException("Máy chủ AI trả về dữ liệu không đọc được.", ex);
        }
        if (string.IsNullOrWhiteSpace(text)) throw new CertificateReadException("AI không trả về kết quả đọc giấy.");

        try
        {
            using var document = JsonDocument.Parse(StripCodeFence(text));
            var root = document.RootElement;

            string? Text(string name, int max = 250)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
                var cleaned = Regex.Replace(value.GetString() ?? "", @"\s+", " ").Trim().Trim('"', '\'');
                if (cleaned.Length == 0 || EmptyWordSet.Contains(cleaned)) return null;
                return cleaned.Length <= max ? cleaned : cleaned[..max];
            }

            bool Flag(string name, bool fallback) => root.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : fallback,
                    _ => fallback,
                }
                : fallback;

            var reading = new CertificateReading(
                IsCertificate: Flag("isCertificate", false),
                IsReadable: Flag("isReadable", true),
                DocumentType: Text("documentType", 120),
                HolderName: Text("holderName", 120),
                Title: Text("title"),
                Issuer: Text("issuer"),
                CertificateNo: Text("certificateNo", 100),
                IssuedAt: ParseDate(Text("issuedAt", 60)),
                ExpiredAt: ParseDate(Text("expiredAt", 60)),
                Note: null);

            _logger.LogInformation("AI ({Model}) doc chung nhan: loai={Type}, la chung nhan={IsCertificate}, doc ro={IsReadable}",
                Model, reading.DocumentType, reading.IsCertificate, reading.IsReadable);
            return reading;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("AI ({Model}) tra ve JSON hong khi doc chung nhan: {Text}", Model, text);
            throw new CertificateReadException("AI trả về kết quả đọc giấy không hợp lệ.", ex);
        }
    }

    /// <summary>Ngày model đọc được → DateTime. Nhận dạng ISO như đã dặn, và cả kiểu ngày/tháng/năm in trên giấy
    /// phòng khi model chép nguyên văn. Năm vô lý (đọc nhầm số) thì coi như không đọc được.</summary>
    private static DateTime? ParseDate(string? text)
    {
        if (text is null) return null;

        var vietnamese = Regex.Match(text, @"ngày\s*(\d{1,2})\s*tháng\s*(\d{1,2})\s*năm\s*(\d{4})", RegexOptions.IgnoreCase);
        if (vietnamese.Success)
            text = $"{vietnamese.Groups[3].Value}-{vietnamese.Groups[2].Value}-{vietnamese.Groups[1].Value}";

        if (!DateTime.TryParseExact(text.Trim(), DateFormatList, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return date.Year >= 1950 && date.Year <= DateTime.Today.Year + 50 ? date.Date : null;
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd > 0 && lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence].Trim() : trimmed;
    }

    // ==================== PDF ====================

    private (List<byte[]> ImageList, string Text) ExtractPdf(byte[] content)
    {
        var imageList = new List<byte[]>();
        var text = new StringBuilder();
        try
        {
            using var document = PdfDocument.Open(content);
            foreach (var page in document.GetPages().Take(MaxPdfPage))
            {
                var pageText = PageTextByLine(page);
                if (pageText.Length > 0) text.AppendLine(pageText);

                // Ảnh lớn nhất trang mới là bản quét; ảnh nhỏ là logo, con dấu, chữ ký.
                var scan = page.GetImages()
                    .Where(x => x.WidthInSamples >= MinScanImageSide && x.HeightInSamples >= MinScanImageSide * 3 / 4)
                    .OrderByDescending(x => (long)x.WidthInSamples * x.HeightInSamples)
                    .FirstOrDefault();
                if (scan is not null && ImageByte(scan) is { } imageByte) imageList.Add(imageByte);
            }
        }
        catch (Exception ex)
        {
            // PDF hỏng / có mật khẩu: coi như không có gì đọc được, nơi gọi báo nhân viên gửi ảnh.
            _logger.LogWarning(ex, "Khong doc duoc tep PDF chung nhan.");
        }

        var allText = Regex.Replace(text.ToString(), @"[ \t]+", " ").Trim();
        return (imageList, allText.Length <= MaxPdfTextCharacter ? allText : allText[..MaxPdfTextCharacter]);
    }

    /// <summary>Chữ của trang theo TỪNG DÒNG như mắt nhìn (trên xuống, trái sang). Nối thẳng các từ theo thứ tự trong
    /// tệp thì dòng "Chứng nhận Ông/Bà: QUAN VŨ" dính vào "Sinh ngày..." của dòng dưới, và model đọc ra họ tên
    /// "QUAN VŨ Sinh" — không còn ranh giới dòng thì model không biết ô nào kết thúc ở đâu.</summary>
    private static string PageTextByLine(Page page)
    {
        var lineList = new List<List<Word>>();
        foreach (var word in page.GetWords().OrderByDescending(x => x.BoundingBox.Bottom))
        {
            var current = lineList.Count > 0 ? lineList[^1] : null;
            // Cùng dòng khi đáy chữ lệch nhau chưa tới nửa chiều cao chữ (chữ to nhỏ khác nhau vẫn chung một dòng).
            if (current is not null && Math.Abs(current[0].BoundingBox.Bottom - word.BoundingBox.Bottom)
                < Math.Max(2, Math.Min(current[0].BoundingBox.Height, word.BoundingBox.Height) * 0.5))
                current.Add(word);
            else
                lineList.Add([word]);
        }
        return string.Join('\n', lineList.Select(JoinLine).Where(line => line.Length > 0));
    }

    /// <summary>Nối các từ của một dòng; khoảng trống ngang lớn (hai cột chung một hàng — "Số: 1234/GCN" bên trái,
    /// "TP. Hồ Chí Minh, ngày..." bên phải) thành dấu " | ", không thì model đọc số hiệu dính luôn tên thành phố.</summary>
    private static string JoinLine(List<Word> line)
    {
        var builder = new StringBuilder();
        Word? previous = null;
        foreach (var word in line.OrderBy(x => x.BoundingBox.Left))
        {
            if (previous is not null)
            {
                var characterWidth = previous.BoundingBox.Width / Math.Max(1, previous.Text.Length);
                var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                builder.Append(gap > Math.Max(24, characterWidth * 4) ? " | " : " ");
            }
            builder.Append(word.Text);
            previous = word;
        }
        return builder.ToString().Trim();
    }

    /// <summary>Byte ảnh gửi được cho model: ảnh JPEG nhúng (DCTDecode) lấy nguyên, kiểu khác đổi sang PNG nếu
    /// PdfPig giải được (JPEG 2000, JBIG2 thì không — trả null).</summary>
    private static byte[]? ImageByte(IPdfImage image)
    {
        var raw = image.RawBytes;
        if (raw.Count > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF) return raw.ToArray();
        try
        {
            return image.TryGetPng(out var png) ? png : null;
        }
        catch
        {
            return null;
        }
    }
}
