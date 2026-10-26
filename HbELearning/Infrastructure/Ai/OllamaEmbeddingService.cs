// File: Infrastructure/Ai/OllamaEmbeddingService.cs
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HbELearning.Infrastructure.Ai;

/// <summary>Gọi Ollama chạy tại máy chủ nội bộ (mặc định http://localhost:11434) để sinh vector nhúng.
/// Chạy offline, không tốn phí API và dữ liệu tài liệu nội bộ không ra khỏi hệ thống.
/// Cài model trước khi dùng: <c>ollama pull nomic-embed-text</c>.</summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    /// <summary>Câu báo lỗi hiển thị trên màn hình khi máy chủ AI không dùng được. Cố ý không nêu địa chỉ
    /// máy chủ, tên model hay lệnh cần chạy — những chi tiết đó chỉ ghi vào log máy chủ cho quản trị viên.</summary>
    private const string UnavailableMessage = "Chưa dùng được tính năng AI. Vui lòng liên hệ quản trị hệ thống.";

    private readonly HttpClient _http;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly string _baseUrl;
    private readonly string _keepAlive;

    /// <summary>Ollama bản cũ (&lt; 0.3) chưa có endpoint /api/embed nhận nhiều đoạn một lúc.
    /// Gặp 404 một lần thì nhớ luôn để các lần sau đi thẳng endpoint cũ /api/embeddings.</summary>
    private static bool _useLegacyEndpoint;

    public string Model { get; }

    public OllamaEmbeddingService(HttpClient http, IConfiguration config, ILogger<OllamaEmbeddingService> logger)
    {
        _http = http;
        _logger = logger;

        _baseUrl = (config["Ollama:BaseUrl"] is { Length: > 0 } url ? url : "http://localhost:11434").TrimEnd('/');
        Model = config["Ollama:EmbeddingModel"] is { Length: > 0 } model ? model : "nomic-embed-text";

        // Lần gọi đầu tiên Ollama phải nạp model vào RAM nên có thể mất hàng chục giây — timeout mặc định 100s là quá ngắn.
        // Model nhúng bé (khoảng 270MB) nhưng được gọi ở MỌI câu hỏi để mã hóa câu hỏi thành vector.
        // Để Ollama nhả nó sau 5 phút mặc định thì câu hỏi đầu tiên sau mỗi quãng nghỉ phải chờ nạp lại
        // trước cả khi bắt đầu tìm tài liệu.
        _keepAlive = config["Ollama:KeepAlive"] is { Length: > 0 } keepAlive ? keepAlive : "60m";

        var timeoutSecond = config.GetValue<int?>("Ollama:TimeoutSeconds") ?? 300;
        _http.Timeout = TimeSpan.FromSeconds(timeoutSecond);
    }

    /// <summary>Nạp sẵn model nhúng vào RAM để câu hỏi đầu tiên không phải chờ nạp.</summary>
    public Task WarmUpAsync(CancellationToken cancellationToken)
        => GetEmbeddingAsync("khởi động", cancellationToken);

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await GetEmbeddingListAsync([text], cancellationToken);
        return result.Count > 0 ? result[0] : [];
    }

    public async Task<List<float[]>> GetEmbeddingListAsync(IReadOnlyList<string> textList, CancellationToken cancellationToken = default)
    {
        if (textList.Count == 0) return [];

        if (!_useLegacyEndpoint)
        {
            try
            {
                return await CallEmbedAsync(textList, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Ollama khong ho tro /api/embed, chuyen sang endpoint cu /api/embeddings.");
                _useLegacyEndpoint = true;
            }
        }

        // Endpoint cũ chỉ nhận 1 đoạn mỗi lần gọi.
        var resultList = new List<float[]>(textList.Count);
        foreach (var text in textList)
            resultList.Add(await CallLegacyEmbeddingsAsync(text, cancellationToken));

        return resultList;
    }

    public async Task<AiDocumentStatusDto> CheckStatusAsync(string? chatModel = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Chờ ngắn thôi: đây là kiểm tra "máy chủ có sống không", không phải lần gọi sinh vector.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _http.GetAsync($"{_baseUrl}/api/tags", timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var nameList = JsonNode.Parse(json)?["models"]?.AsArray()
                .Select(x => x?["model"]?.GetValue<string>() ?? string.Empty)
                .Where(x => x.Length > 0)
                .ToList() ?? [];

            // Ollama đặt tên kèm nhãn phiên bản ("nomic-embed-text:latest") nên so khớp cả dạng có và không có nhãn.
            bool IsInstalled(string name) =>
                nameList.Any(x => x == name || x.StartsWith($"{name}:", StringComparison.OrdinalIgnoreCase));

            var embeddingReady = IsInstalled(Model);
            var chatReady = chatModel is { Length: > 0 } && IsInstalled(chatModel);

            if (!embeddingReady)
            {
                _logger.LogWarning(
                    "Ollama tai {BaseUrl} dang chay nhung chua cai model embedding {Model}. Chay lenh: ollama pull {Model}",
                    _baseUrl, Model, Model);
                return new AiDocumentStatusDto(false, _baseUrl, Model, UnavailableMessage, chatModel, chatReady);
            }

            // Thiếu model sinh câu trả lời thì vẫn nạp tài liệu được, chỉ mất phần hỏi đáp — nói rõ giới hạn
            // đó cho người xem, còn tên model cần cài thì để quản trị viên đọc trong log máy chủ.
            if (chatModel is { Length: > 0 } && !chatReady)
            {
                _logger.LogWarning(
                    "Ollama tai {BaseUrl} chua cai model sinh cau tra loi {ChatModel}. Chay lenh: ollama pull {ChatModel}",
                    _baseUrl, chatModel, chatModel);
                return new AiDocumentStatusDto(true, _baseUrl, Model,
                    "Nạp tài liệu được, nhưng chưa hỏi đáp được. Vui lòng liên hệ quản trị hệ thống.",
                    chatModel, chatReady);
            }

            return new AiDocumentStatusDto(true, _baseUrl, Model, "Tôi đã sẵn sàng học bài mới.", chatModel, chatReady);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong ket noi duoc Ollama tai {BaseUrl}.", _baseUrl);
            return new AiDocumentStatusDto(false, _baseUrl, Model, UnavailableMessage, chatModel, false);
        }
    }

    /// <summary>POST /api/embed — nhận nhiều đoạn, trả về "embeddings": [[...], [...]].</summary>
    private async Task<List<float[]>> CallEmbedAsync(IReadOnlyList<string> textList, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { model = Model, input = textList, keep_alive = _keepAlive });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync($"{_baseUrl}/api/embed", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var array = JsonNode.Parse(json)?["embeddings"]?.AsArray()
            ?? throw new InvalidOperationException("Ollama tra ve du lieu khong hop le (thieu truong 'embeddings').");

        return [.. array.Select(node => ReadVector(node))];
    }

    /// <summary>POST /api/embeddings — endpoint cũ, mỗi lần 1 đoạn, trả về "embedding": [...].</summary>
    private async Task<float[]> CallLegacyEmbeddingsAsync(string text, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { model = Model, prompt = text, keep_alive = _keepAlive });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync($"{_baseUrl}/api/embeddings", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadVector(JsonNode.Parse(json)?["embedding"]);
    }

    private static float[] ReadVector(JsonNode? node)
    {
        var array = node?.AsArray() ?? throw new InvalidOperationException("Ollama tra ve vector rong.");
        return [.. array.Select(x => x?.GetValue<float>() ?? 0f)];
    }

    /// <summary>Ném lỗi kèm nội dung Ollama trả về — thường là "model 'xxx' not found, try pulling it first",
    /// thông tin cần thiết mà EnsureSuccessStatusCode() mặc định nuốt mất.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Ollama tra ve {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }
}
