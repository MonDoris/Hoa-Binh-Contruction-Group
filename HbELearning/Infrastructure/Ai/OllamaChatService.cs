// File: Infrastructure/Ai/OllamaChatService.cs
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HbELearning.Infrastructure.Ai;

/// <summary>Sinh câu trả lời bằng model ngôn ngữ chạy nội bộ qua Ollama, CHỈ dựa trên các đoạn tài liệu
/// nội bộ được đưa vào (RAG) — không để model tự bịa theo kiến thức nền của nó.</summary>
public class OllamaChatService : IAiChatService
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaChatService> _logger;
    private readonly string _baseUrl;
    private readonly int _maxAnswerToken;
    private readonly int _maxContextCharacter;
    private readonly int _contextWindow;
    private readonly string _keepAlive;

    public string Model { get; }

    public OllamaChatService(HttpClient http, IConfiguration config, ILogger<OllamaChatService> logger)
    {
        _http = http;
        _logger = logger;

        _baseUrl = (config["Ollama:BaseUrl"] is { Length: > 0 } url ? url : "http://localhost:11434").TrimEnd('/');

        // Hỏi đáp là chỗ DUY NHẤT người dùng ngồi chờ trước màn hình, nên nó được dùng model riêng, nhỏ hơn
        // model soạn bài. Bỏ trống AnswerModel thì quay về dùng chung ChatModel như trước.
        Model = config["Ollama:AnswerModel"] is { Length: > 0 } answerModel ? answerModel
            : config["Ollama:ChatModel"] is { Length: > 0 } model ? model : "qwen2.5:7b";

        _maxAnswerToken = config.GetValue<int?>("Ollama:MaxAnswerToken") ?? 400;
        _maxContextCharacter = config.GetValue<int?>("Ollama:AnswerContextCharacter") ?? 2000;
        _contextWindow = config.GetValue<int?>("Ollama:AnswerContextToken") ?? 2048;
        _keepAlive = config["Ollama:KeepAlive"] is { Length: > 0 } keepAlive ? keepAlive : "60m";

        _http.Timeout = TimeSpan.FromSeconds(config.GetValue<int?>("Ollama:TimeoutSeconds") ?? 300);
    }

    public List<DocumentChunkMatchDto> TrimToPromptBudget(IReadOnlyList<DocumentChunkMatchDto> contextList)
    {
        // Ưu tiên giữ ĐOẠN KHỚP CÂU HỎI (Score > 0) trước, rồi mới lấy đoạn liền kề (Score = 0, do
        // GetAnswerContextAsync bù vào cho trọn ý) lấp phần ngân sách còn thừa.
        //
        // Cắt thẳng theo thứ tự danh sách thì hỏng: danh sách đã được xếp lại theo THỨ TỰ ĐỌC trong tài
        // liệu, nên đoạn khớp nhất có thể nằm sau vài đoạn liền kề và bị cắt mất đúng lúc ngân sách hẹp lại.
        var pickedList = new List<DocumentChunkMatchDto>();
        var usedCharacter = 0;

        foreach (var chunk in contextList.OrderByDescending(x => x.Score))
        {
            var length = chunk.TextContent.Trim().Length;

            // Đoạn đầu tiên luôn được giữ dù có vượt trần: thà đọc lâu còn hơn gửi cho model một câu lệnh
            // rỗng phần TÀI LIỆU, vì khi đó nó buộc phải trả lời "chưa có thông tin" dù kho có tài liệu.
            if (pickedList.Count > 0 && usedCharacter + length > _maxContextCharacter) break;

            pickedList.Add(chunk);
            usedCharacter += length;
        }

        // Trả lại đúng thứ tự đọc như bản gốc — đọc lộn xộn thì model dễ ghép nhầm thông tin của hai đoạn
        // rời rạc thành một câu sai.
        var keptIdSet = pickedList.Select(x => x.Id).ToHashSet();
        return [.. contextList.Where(x => keptIdSet.Contains(x.Id))];
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string question,
        IReadOnlyList<DocumentChunkMatchDto> contextList,
        IReadOnlyList<AiChatTurnDto>? historyList = null,
        bool isCitationEnabled = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var userPrompt = BuildUserPrompt(question, contextList, _maxContextCharacter);

        // Ước lượng 3 ký tự một token (tiếng Việt có dấu tốn token hơn tiếng Anh). Vượt cửa sổ ngữ cảnh thì
        // Ollama lặng lẽ bỏ phần ĐẦU câu lệnh — tức đúng đoạn tài liệu — và câu trả lời sẽ thiếu mà không ai
        // biết vì sao. Báo ra log để người chỉnh cấu hình biết phải nâng AnswerContextToken theo.
        var historyCharacter = historyList?.Sum(x => x.Question.Length + Math.Min(x.Answer.Length, MaxHistoryAnswerCharacter)) ?? 0;
        var estimatedToken = (userPrompt.Length + BuildSystemPrompt(isCitationEnabled).Length + historyCharacter) / 3 + _maxAnswerToken;
        if (estimatedToken > _contextWindow)
            _logger.LogWarning(
                "Cau lenh hoi dap uoc chung {Estimated} token, vuot cua so ngu canh {Window}. " +
                "Nang Ollama:AnswerContextToken hoac ha Ollama:AnswerContextCharacter, neu khong Ollama se cat bot phan tai lieu.",
                estimatedToken, _contextWindow);

        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            stream = true,

            // Giữ model nằm sẵn trong bộ nhớ giữa các câu hỏi. Mặc định của Ollama là nhả sau 5 phút, mà
            // nạp lại một model đo được mất hơn 12 giây — nghĩa là câu hỏi đầu mỗi buổi sáng, đầu giờ chiều
            // hay sau mỗi cuộc họp đều cõng thêm ngần ấy giây trước khi model đọc được chữ nào.
            keep_alive = _keepAlive,

            messages = BuildMessageList(userPrompt, historyList, isCitationEnabled),
            options = new
            {
                // Nhiệt độ thấp: việc ở đây là thuật lại đúng quy định của công ty, không phải sáng tác.
                temperature = 0.1,
                num_predict = _maxAnswerToken,

                // Khai báo rõ cửa sổ ngữ cảnh: để mặc định thì Ollama có thể cắt cụt phần tài liệu dài
                // mà không báo gì, và câu trả lời sẽ thiếu đúng đoạn vừa bị cắt.
                //
                // Đặt vừa đủ chứ không rộng rãi: cửa sổ càng lớn thì bộ nhớ đệm KV cấp phát càng nhiều và
                // mỗi token sinh ra càng chậm. Vừa đủ ở đây là phần tài liệu (AnswerContextCharacter) cộng
                // câu lệnh hệ thống cộng phần trả lời (MaxAnswerToken) — xem appsettings.json.
                //
                // ĐỂ NGUYÊN MỘT CON SỐ CỐ ĐỊNH, đừng tính động theo từng câu hỏi: đổi num_ctx giữa hai lượt
                // gọi là Ollama dựng lại runner, tức trả thêm hơn 12 giây nạp model cho mỗi lần đổi.
                num_ctx = _contextWindow
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Ollama trả về {(int)response.StatusCode}: {body}", null, response.StatusCode);
        }

        // Ollama trả về NDJSON: mỗi dòng là một JSON chứa một mẩu chữ.
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Đọc tới khi ReadLineAsync trả null (hết luồng) — EndOfStream sẽ chặn luồng theo kiểu đồng bộ.
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? piece;
            bool done;
            try
            {
                var node = JsonNode.Parse(line);
                piece = node?["message"]?["content"]?.GetValue<string>();
                done = node?["done"]?.GetValue<bool>() ?? false;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Bo qua dong NDJSON khong doc duoc tu Ollama: {Line}", line);
                continue;
            }

            if (!string.IsNullOrEmpty(piece)) yield return piece;
            if (done) yield break;
        }
    }

    /// <summary>Số lượt hỏi-đáp cũ được nhắc lại cho model. Hai lượt là đủ để bắt được những câu hỏi nối
    /// tiếp thông thường, mà vẫn rẻ: mỗi lượt cũ là chừng ấy chữ model phải ĐỌC LẠI ở mọi câu hỏi sau đó,
    /// và trên máy chạy CPU thì thời gian đọc mới là phần tốn nhất chứ không phải thời gian viết.</summary>
    private const int MaxHistoryTurn = 2;

    /// <summary>Trần độ dài phần câu trả lời cũ được nhắc lại. Câu trả lời đầy đủ dài gấp nhiều lần câu hỏi;
    /// nhắc lại trọn vẹn thì hai lượt cũ đã ăn hết ngân sách dành cho TÀI LIỆU của câu hỏi hiện tại — mà tài
    /// liệu mới là thứ quyết định câu trả lời đúng hay sai.</summary>
    private const int MaxHistoryAnswerCharacter = 400;

    /// <summary>Dựng chuỗi tin nhắn gửi Ollama: câu lệnh hệ thống, vài lượt hỏi-đáp cũ, rồi câu hỏi hiện tại.
    ///
    /// Lượt cũ được đưa vào đúng dạng hội thoại (user/assistant) chứ không dán vào trong câu hỏi mới: model
    /// hội thoại được huấn luyện theo đúng cấu trúc này, còn dán tay thì nó dễ hiểu nhầm phần chữ cũ là một
    /// phần của câu hỏi đang phải trả lời.</summary>
    private static object[] BuildMessageList(
        string userPrompt, IReadOnlyList<AiChatTurnDto>? historyList, bool isCitationEnabled)
    {
        var messageList = new List<object> { new { role = "system", content = BuildSystemPrompt(isCitationEnabled) } };

        foreach (var turn in (historyList ?? []).TakeLast(MaxHistoryTurn))
        {
            if (string.IsNullOrWhiteSpace(turn.Question) || string.IsNullOrWhiteSpace(turn.Answer)) continue;

            messageList.Add(new { role = "user", content = turn.Question.Trim() });
            messageList.Add(new
            {
                role = "assistant",
                content = turn.Answer.Length > MaxHistoryAnswerCharacter
                    ? turn.Answer[..MaxHistoryAnswerCharacter] + "..."
                    : turn.Answer
            });
        }

        messageList.Add(new { role = "user", content = userPrompt });
        return [.. messageList];
    }

    /// <summary>Giữ thật ngắn: mỗi chữ trong câu lệnh hệ thống cũng là chữ model phải đọc lại ở MỌI câu hỏi,
    /// mà trên CPU thời gian đọc mới là phần tốn nhất.</summary>
    private static string BuildSystemPrompt(bool isCitationEnabled) => $"""
        Bạn là trợ lý đào tạo nội bộ, trả lời câu hỏi của nhân viên bằng tiếng Việt.
        - Phần TÀI LIỆU gồm tài liệu nội bộ và nội dung các bài học của khóa nhân viên đang học.
        - Chỉ dùng thông tin trong phần TÀI LIỆU. Không suy đoán, không thêm số liệu tài liệu không nói.
        - Không có thông tin thì trả lời đúng câu: "Tài liệu nội bộ hiện chưa có thông tin về nội dung này."
        - Câu hỏi nhiều ý thì trả lời lần lượt từng ý.
        {(isCitationEnabled
            ? "- Ghi số nguồn trong ngoặc vuông sau mỗi thông tin, ví dụ [1]."
            : "- Không ghi số nguồn, không nhắc tên tài liệu hay tên bài học trong câu trả lời.")}
        - Ngắn gọn, vào thẳng câu trả lời, không nhắc lại câu hỏi.
        """;

    /// <summary><paramref name="maxContextCharacter"/> là trần số chữ tài liệu được nhét vào câu lệnh — dao
    /// chỉnh tốc độ chính của tính năng hỏi đáp. Đo trên máy chạy CPU: model đọc chỉ khoảng 12 token/giây,
    /// tức mỗi 1000 chữ tài liệu tốn thêm chừng 30 giây TRƯỚC KHI hiện ra chữ trả lời đầu tiên. Đưa vào ít
    /// mà đúng thì nhanh hơn hẳn và không kém chính xác — phần chọn đoạn nào đã do tìm kiếm ngữ nghĩa lo.</summary>
    private static string BuildUserPrompt(
        string question, IReadOnlyList<DocumentChunkMatchDto> contextList, int maxContextCharacter)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TÀI LIỆU:");

        var usedCharacter = 0;
        for (int i = 0; i < contextList.Count; i++)
        {
            var item = contextList[i];
            if (usedCharacter + item.TextContent.Length > maxContextCharacter) break;

            builder.AppendLine($"[{i + 1}] (nguồn: {item.SourceFileName})");
            builder.AppendLine(item.TextContent);
            builder.AppendLine();
            usedCharacter += item.TextContent.Length;
        }

        builder.AppendLine("CÂU HỎI CỦA NHÂN VIÊN:");
        builder.Append(question);
        return builder.ToString();
    }
}
