// File: Infrastructure/Ai/OllamaContentService.cs
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HbELearning.Infrastructure.Ai;

/// <summary>
/// Soạn nội dung bài học, câu hỏi kiểm tra và bản dịch giao diện bằng model ngôn ngữ CHẠY NỘI BỘ qua Ollama.
///
/// Toàn bộ dữ liệu — tiêu đề bài học, câu chữ giao diện, và nhất là các đoạn trích tài liệu nội bộ của công
/// ty — ở lại trong mạng nội bộ, không gửi ra nhà cung cấp bên ngoài nào.
///
/// Đổi lại là tốc độ. Đo trên máy chạy CPU thuần: đọc câu lệnh khoảng 22 token/giây, sinh chữ khoảng 5
/// token/giây. Một bài học có bám tài liệu công ty mất chừng 4-6 phút, một lô 40 khóa dịch mất chừng 3-4
/// phút. Vì vậy lớp này cắt bớt phần tài liệu nhét vào câu lệnh (xem <see cref="MaxContextCharacter"/>) và
/// đặt hạn chờ rộng hơn hẳn dịch vụ hỏi đáp — chứ không phải nhét vào bao nhiêu cũng được.
/// </summary>
public class OllamaContentService : IAiContentService
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaContentService> _logger;
    private readonly string _baseUrl;
    private readonly int _maxContentToken;
    private readonly int _contextWindow;
    private readonly string _keepAlive;

    public string Model { get; }

    public bool IsConfigured => _baseUrl.Length > 0 && Model.Length > 0;

    public OllamaContentService(HttpClient http, IConfiguration config, ILogger<OllamaContentService> logger)
    {
        _http = http;
        _logger = logger;

        _baseUrl = (config["Ollama:BaseUrl"] is { Length: > 0 } url ? url : "http://localhost:11434").TrimEnd('/');
        Model = config["Ollama:ChatModel"] is { Length: > 0 } model ? model : "qwen2.5:7b";

        _maxContentToken = config.GetValue<int?>("Ollama:MaxContentToken") ?? 2048;
        _contextWindow = config.GetValue<int?>("Ollama:ContentContextToken") ?? 8192;

        // Giữ model nằm sẵn trong bộ nhớ giữa các lượt soạn. Nạp lại một model 7B đo được mất hơn 12 giây,
        // mà soạn bài học thường đi thành chuỗi: người soạn tạo bài, đọc lại, sửa chủ đề rồi tạo tiếp.
        _keepAlive = config["Ollama:KeepAlive"] is { Length: > 0 } keepAlive ? keepAlive : "60m";

        // Hạn chờ riêng, rộng hơn nhiều so với hỏi đáp tài liệu: hỏi đáp trả lời vài trăm chữ, còn ở đây là
        // soạn cả một bài học hoặc dịch 40 câu một lượt. Đặt 300 giây như bên kia thì mọi bài học dài đều
        // đứt giữa chừng, mà người soạn bài chỉ thấy "lỗi mạng" chứ không biết là do chờ chưa đủ lâu.
        _http.Timeout = TimeSpan.FromSeconds(config.GetValue<int?>("Ollama:ContentTimeoutSeconds") ?? 1800);
    }

    /// <summary>Hồ sơ độ dài của một bài học do AI soạn.
    ///
    /// <para><paramref name="MinBodyWord"/> là NGƯỠNG PHÁT HIỆN DÀN Ý: dưới ngưỡng này nghĩa là bài trả về
    /// mới có tiêu đề chứ chưa có nội dung, và <see cref="GenerateLessonContentAsync"/> sẽ bắt model viết
    /// lại lần hai. Đặt vào khoảng 60% cận dưới độ dài yêu cầu — ngắn hơn mức đó thì dù có chữ vẫn chưa đủ
    /// để học viên tự học được.</para></summary>
    private sealed record LessonDepthProfile(string Key, int MinWord, int MaxWord, int MinBodyWord, int MaxToken);

    /// <summary>Ba mức độ dài người soạn bài chọn được trên giao diện.
    ///
    /// Trần token đặt rộng hơn số từ khá nhiều (tiếng Việt tốn chừng 3 token mỗi từ, cộng phần thẻ HTML):
    /// đặt sát quá thì model bị cắt ngang giữa câu đúng lúc đang viết phần cuối bài.</summary>
    private static LessonDepthProfile ResolveDepthProfile(string? detailLevel) => detailLevel?.Trim().ToLowerInvariant() switch
    {
        "short" => new LessonDepthProfile("short", 400, 700, 260, 1800),
        "detailed" => new LessonDepthProfile("detailed", 1200, 1800, 780, 5120),
        _ => new LessonDepthProfile("standard", 700, 1100, 450, 3200),
    };

    public async Task<LessonContentResult> GenerateLessonContentAsync(string? courseTitle, string lessonTitle,
        string? topic, string? instructions, IReadOnlyList<DocumentChunkMatchDto>? companyContextList = null,
        string? detailLevel = null, CancellationToken cancellationToken = default)
    {
        var depth = ResolveDepthProfile(detailLevel);
        var companySection = companyContextList is { Count: > 0 } ? BuildCompanyContextSection(companyContextList) : "";

        var firstReply = await CallOllamaAsync(
            BuildLessonPrompt(courseTitle, lessonTitle, topic, instructions, companySection, depth),
            responseSchema: null, cancellationToken, depth.MaxToken);

        var html = NormalizeLessonHtml(firstReply.Text, firstReply.IsTruncated);
        var bodyWordCount = CountBodyWord(html);
        if (bodyWordCount >= depth.MinBodyWord)
            return new LessonContentResult(html, bodyWordCount, WasExpanded: false, firstReply.IsTruncated);

        // Model cỡ 7B rất hay trả về ĐÚNG BỘ TIÊU ĐỀ rồi coi như xong bài — đây là lỗi hay gặp nhất của tính
        // năng này. Cách chữa hiệu quả hơn hẳn việc gọi lại y nguyên câu lệnh cũ: đưa chính bản nháp đó lại
        // cho model và bắt viết đầy nội dung cho từng tiêu đề. Bộ khung đã chốt sẵn nên model không phải vừa
        // nghĩ dàn ý vừa viết, và lần này nó viết thật.
        _logger.LogWarning("Bai hoc AI ({Model}) chi co {Word} tu than bai, duoi nguong {Min} - bat model viet lai lan hai.",
            Model, bodyWordCount, depth.MinBodyWord);

        try
        {
            var secondReply = await CallOllamaAsync(
                BuildExpandLessonPrompt(lessonTitle, html, companySection, depth),
                responseSchema: null, cancellationToken, depth.MaxToken);

            var expandedHtml = NormalizeLessonHtml(secondReply.Text, secondReply.IsTruncated);
            var expandedWordCount = CountBodyWord(expandedHtml);

            // Chỉ nhận bản viết lại khi nó THỰC SỰ dày hơn bản đầu: model đôi khi chép lại gần nguyên dàn ý
            // cũ, lấy bừa bản sau là đổi một bài sơ sài lấy một bài sơ sài hơn.
            if (expandedWordCount > bodyWordCount)
                return new LessonContentResult(expandedHtml, expandedWordCount, WasExpanded: true, secondReply.IsTruncated);
        }
        catch (Exception ex)
        {
            // Lượt viết lại hỏng thì vẫn trả bản đầu: bài sơ sài còn sửa tay được, còn ném lỗi ra là người
            // soạn bài mất trắng mấy phút đã chờ của lượt đầu.
            _logger.LogError(ex, "Viet lai bai hoc AI that bai, giu lai ban dau.");
        }

        return new LessonContentResult(html, bodyWordCount, WasExpanded: false, firstReply.IsTruncated);
    }

    public async IAsyncEnumerable<LessonStreamPiece> StreamLessonContentAsync(string? courseTitle, string lessonTitle,
        string? topic, string? instructions, IReadOnlyList<DocumentChunkMatchDto>? companyContextList = null,
        string? detailLevel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var depth = ResolveDepthProfile(detailLevel);
        var companySection = companyContextList is { Count: > 0 } ? BuildCompanyContextSection(companyContextList) : "";

        var firstTail = new StreamTail();
        var firstBuilder = new StringBuilder();

        await foreach (var token in CallOllamaStreamAsync(
            BuildLessonPrompt(courseTitle, lessonTitle, topic, instructions, companySection, depth),
            depth.MaxToken, firstTail, cancellationToken))
        {
            firstBuilder.Append(token);
            yield return LessonStreamPiece.FromToken(token);
        }

        var html = NormalizeLessonHtml(firstBuilder.ToString(), firstTail.IsTruncated);
        var bodyWordCount = CountBodyWord(html);

        if (bodyWordCount >= depth.MinBodyWord)
        {
            yield return LessonStreamPiece.FromResult(
                new LessonContentResult(html, bodyWordCount, WasExpanded: false, firstTail.IsTruncated));
            yield break;
        }

        // Giống hệt lý do ở GenerateLessonContentAsync: model hay trả về đúng bộ tiêu đề rồi coi như xong bài,
        // và cách chữa là đưa chính bản nháp đó lại cho model bắt viết đầy nội dung cho từng tiêu đề.
        _logger.LogWarning("Bai hoc AI ({Model}) chi co {Word} tu than bai, duoi nguong {Min} - bat model viet lai lan hai.",
            Model, bodyWordCount, depth.MinBodyWord);

        yield return LessonStreamPiece.RewriteStart();

        var secondTail = new StreamTail();
        var secondBuilder = new StringBuilder();

        // Duyệt luồng bằng enumerator thủ công chứ không dùng await foreach: cần bắt lỗi giữa chừng để còn
        // giữ lại bản đầu, mà C# không cho đặt "yield return" trong khối try có catch.
        var enumerator = CallOllamaStreamAsync(
            BuildExpandLessonPrompt(lessonTitle, html, companySection, depth),
            depth.MaxToken, secondTail, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                string token;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    token = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Lượt viết lại hỏng thì vẫn trả bản đầu: bài sơ sài còn sửa tay được, còn ném lỗi ra là
                    // người soạn bài mất trắng mấy phút đã chờ của lượt đầu.
                    _logger.LogError(ex, "Viet lai bai hoc AI that bai, giu lai ban dau.");
                    secondBuilder.Clear();
                    break;
                }

                secondBuilder.Append(token);
                yield return LessonStreamPiece.FromToken(token);
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (secondBuilder.Length > 0)
        {
            var expandedHtml = NormalizeLessonHtml(secondBuilder.ToString(), secondTail.IsTruncated);
            var expandedWordCount = CountBodyWord(expandedHtml);

            // Chỉ nhận bản viết lại khi nó THỰC SỰ dày hơn bản đầu — model đôi khi chép lại gần nguyên dàn ý cũ.
            if (expandedWordCount > bodyWordCount)
            {
                yield return LessonStreamPiece.FromResult(
                    new LessonContentResult(expandedHtml, expandedWordCount, WasExpanded: true, secondTail.IsTruncated));
                yield break;
            }
        }

        // Bản viết lại hỏng hoặc không dày hơn: quay về bản đầu. Nơi gọi đã xóa phần đang hiện ở mốc
        // RewriteStart nên phải nhận lại bài từ Result này chứ không tự ghép phần chữ đã trôi qua.
        yield return LessonStreamPiece.FromResult(
            new LessonContentResult(html, bodyWordCount, WasExpanded: false, firstTail.IsTruncated));
    }

    /// <summary>Câu lệnh soạn bài học.
    ///
    /// Bản trước chỉ dặn "viết nội dung đầy đủ, có cấu trúc" — với model chạy nội bộ cỡ 7B, câu dặn chung
    /// chung như vậy gần như luôn cho ra một DÀN Ý: bốn năm tiêu đề, mỗi tiêu đề một gạch đầu dòng ba chữ.
    /// Vì vậy ở đây nêu thẳng từng phần phải có, mỗi phần dài bao nhiêu đoạn, mỗi gạch đầu dòng tối thiểu
    /// bao nhiêu từ, và cấm hẳn lối viết mục lục. Ràng buộc càng ĐẾM ĐƯỢC thì model càng làm đúng.</summary>
    private static string BuildLessonPrompt(string? courseTitle, string lessonTitle, string? topic,
        string? instructions, string companySection, LessonDepthProfile depth) => $"""
        Bạn là chuyên gia đào tạo nội bộ doanh nghiệp. Nhiệm vụ: viết MỘT BÀI HỌC HOÀN CHỈNH cho nhân viên tự học trên hệ thống e-learning — người học chỉ đọc bài này, không có giảng viên giảng thêm.

        {(string.IsNullOrWhiteSpace(courseTitle) ? "" : $"Khóa đào tạo: {courseTitle}")}
        Tên bài học: {lessonTitle}
        {(string.IsNullOrWhiteSpace(topic) ? "" : $"Chủ đề / nội dung cần tập trung: {topic}")}
        {(string.IsNullOrWhiteSpace(instructions) ? "" : $"Yêu cầu thêm: {instructions}")}
        {companySection}

        CẤU TRÚC BẮT BUỘC — viết đủ các phần sau, đúng thứ tự, mỗi phần mở đầu bằng một thẻ <h3>:
        1. Mục tiêu bài học: một đoạn <p> 2-3 câu nói rõ bài học này giải quyết vấn đề gì trong công việc, rồi <ul> gồm 3-5 mục tiêu, mỗi mục tiêu là một câu hoàn chỉnh bắt đầu bằng động từ (nhận biết, thực hiện, xử lý...).
        2. Khái niệm và quy định cần nắm: 2-3 đoạn <p> giải thích các thuật ngữ, quy định, thiết bị hoặc biểu mẫu sẽ nhắc tới trong bài.
        3. Từ 2 đến 4 phần nội dung chính, mỗi phần một <h3> đặt tên theo đúng nội dung phần đó. Mỗi phần phải có ít nhất 2 đoạn <p>, mỗi đoạn 3-5 câu; phần nào là quy trình thì thêm <ol> liệt kê từng bước, mỗi bước ghi rõ ai làm, làm gì, dùng thiết bị hay biểu mẫu nào, thế nào là làm đạt.
        4. Tình huống thực tế: 1-2 ví dụ cụ thể tại nơi làm việc, kể thành đoạn văn có bối cảnh, diễn biến và cách xử lý đúng.
        5. Lỗi thường gặp và cách phòng tránh: <ul> 3-5 ý, mỗi ý nêu rõ lỗi, hậu quả và cách phòng tránh.
        6. Tóm tắt: <ul> 3-5 ý cần nhớ.
        7. Câu hỏi tự kiểm tra: <ol> 3 câu hỏi ngắn để người học tự trả lời (KHÔNG kèm đáp án).

        QUY TẮC VIẾT — bắt buộc tuân thủ:
        - Đây là BÀI HỌC HOÀN CHỈNH, KHÔNG PHẢI DÀN Ý, không phải mục lục. Tuyệt đối không để tiêu đề nào đứng trơ: ngay dưới mỗi <h3> phải là nội dung viết đầy đủ thành câu.
        - Không viết những câu rỗng kiểu "nội dung đang cập nhật", "sẽ trình bày sau", "tùy từng đơn vị", "xem thêm tài liệu".
        - Mỗi mục trong <ul> hoặc <ol> là một câu hoàn chỉnh từ 12 từ trở lên, không phải cụm từ hai ba chữ.
        - Tổng độ dài toàn bài khoảng {depth.MinWord}-{depth.MaxWord} từ.
        - Viết bằng tiếng Việt, giọng hướng dẫn, cụ thể, dùng được ngay trong công việc; ưu tiên con số, mốc thời gian, tên biểu mẫu, thao tác cụ thể thay vì nói chung chung.
        - Chỉ dùng các thẻ HTML: h3, h4, p, ul, ol, li, b, i. Không dùng h1, h2, không dùng markdown, không dùng code fence, không thêm lời dẫn hay lời kết ngoài bài học.
        - Trả về DUY NHẤT đoạn HTML nội dung bài học.
        """;

    /// <summary>Câu lệnh lượt hai: giữ nguyên bộ tiêu đề của bản nháp, viết đầy nội dung cho từng tiêu đề.
    ///
    /// Gửi kèm lại phần tài liệu nội bộ — lượt này mới là lượt viết ra phần lớn nội dung thật, cắt tài liệu
    /// đi là đúng chỗ cần bám quy định công ty nhất lại thành model tự nghĩ.</summary>
    private static string BuildExpandLessonPrompt(string lessonTitle, string draftHtml, string companySection,
        LessonDepthProfile depth) => $"""
        Bạn là chuyên gia đào tạo nội bộ doanh nghiệp.

        Dưới đây là BẢN NHÁP bài học "{lessonTitle}". Bản nháp này mới có tiêu đề và vài ý gạch đầu dòng, CHƯA PHẢI một bài học dùng được.

        ===== BẢN NHÁP =====
        {draftHtml}
        ===== HẾT BẢN NHÁP =====
        {companySection}

        Hãy viết lại thành BÀI HỌC HOÀN CHỈNH:
        - Giữ nguyên các tiêu đề <h3> và thứ tự của bản nháp; được thêm tiêu đề mới nếu bài còn thiếu phần quan trọng, nhưng không được bỏ bớt.
        - Với TỪNG tiêu đề, viết ít nhất 2 đoạn <p> (mỗi đoạn 3-5 câu) giải thích đầy đủ; phần nào là quy trình thì thêm <ol> các bước, mỗi bước ghi rõ ai làm, làm gì, dùng thiết bị hay biểu mẫu nào.
        - Mỗi ý gạch đầu dòng của bản nháp phải được viết thành câu hoàn chỉnh từ 12 từ trở lên, kèm giải thích vì sao và làm thế nào.
        - Bổ sung ví dụ tình huống thực tế tại nơi làm việc, các lỗi thường gặp kèm cách phòng tránh, phần tóm tắt và 3 câu hỏi tự kiểm tra ở cuối nếu bản nháp chưa có.
        - Tổng độ dài khoảng {depth.MinWord}-{depth.MaxWord} từ. Không để tiêu đề nào không có nội dung bên dưới.
        - Chỉ dùng các thẻ HTML: h3, h4, p, ul, ol, li, b, i. Không markdown, không code fence, không lời dẫn.
        - Trả về DUY NHẤT đoạn HTML bài học hoàn chỉnh.
        """;

    /// <summary>Phần câu lệnh chứa tài liệu nội bộ của công ty và quy tắc bắt AI bám vào đó.
    ///
    /// Bốn quy tắc dưới đây là toàn bộ giá trị của tính năng này. Chỉ dán tài liệu vào câu lệnh mà không
    /// ràng buộc gì thì model vẫn viết theo kiến thức chung của nó rồi rắc vài chữ lấy từ tài liệu vào —
    /// nhìn thì "có vẻ" bám sát nhưng con số, tên biểu mẫu, số bước quy trình lại là bịa. Nói rõ "không có
    /// trong tài liệu thì viết chung chung và KHÔNG bịa số liệu" là cách duy nhất để người soạn bài phân
    /// biệt được phần nào lấy từ quy định công ty, phần nào chỉ là kiến thức nền.</summary>
    private static string BuildCompanyContextSection(IReadOnlyList<DocumentChunkMatchDto> contextList)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("===== TÀI LIỆU NỘI BỘ CỦA CÔNG TY (nguồn dữ liệu bắt buộc) =====");

        // Dùng TRỌN danh sách nhận được, không tự cắt bớt ở đây: nơi gọi còn hiện chính danh sách này cho
        // người soạn bài xem là "AI đã dựa vào các đoạn sau". Cắt ngầm ở đây thì bảng nguồn kê ra những
        // đoạn model chưa từng đọc — người soạn bài đối chiếu sẽ không hiểu vì sao bài viết không nhắc tới.
        // Việc giới hạn độ dài thuộc về nơi gọi, xem AiService.TrimToPromptBudget.
        var index = 1;
        foreach (var chunk in contextList)
        {
            builder.AppendLine($"[{index}] (Trích từ \"{chunk.SourceFileName}\")");
            builder.AppendLine(chunk.TextContent.Trim());
            builder.AppendLine();
            index++;
        }

        builder.AppendLine("===== HẾT TÀI LIỆU =====");
        builder.AppendLine();
        builder.AppendLine("QUY TẮC BẮT BUỘC khi có tài liệu nội bộ ở trên:");
        builder.AppendLine("1. Nội dung bài học phải BÁM SÁT các tài liệu trên: dùng đúng thuật ngữ, đúng tên biểu mẫu/bộ phận, đúng số liệu, đúng số bước và thứ tự các bước như tài liệu ghi.");
        builder.AppendLine("2. TUYỆT ĐỐI KHÔNG bịa ra con số, thời hạn, mức phạt, tên quy định hay tên phòng ban không có trong tài liệu.");
        builder.AppendLine("3. Phần nào tài liệu chưa đề cập mà vẫn cần cho bài học thì viết ở mức kiến thức chung, không gán cho công ty và không kèm số liệu cụ thể.");
        builder.AppendLine("4. Khi nêu một quy định lấy từ tài liệu, ghi kèm tên tài liệu trong ngoặc đơn ở cuối câu, VD: (theo Nội quy lao động.pdf).");

        return builder.ToString();
    }

    public async Task<List<GeneratedQuestionDto>> GenerateQuizQuestionsAsync(string? courseTitle, string topic, int count, string? difficulty)
    {
        var prompt = $"""
            Bạn là chuyên gia soạn đề kiểm tra đào tạo nội bộ doanh nghiệp.

            {(string.IsNullOrWhiteSpace(courseTitle) ? "" : $"Khóa đào tạo: {courseTitle}")}
            Chủ đề câu hỏi: {topic}
            Số lượng câu hỏi cần tạo: {count}
            {(string.IsNullOrWhiteSpace(difficulty) ? "" : $"Mức độ: {difficulty}")}

            Quy tắc bắt buộc cho từng câu hỏi:
            - questionType = 0 (Một lựa chọn): tạo 4 đáp án, đúng đúng 1 đáp án.
            - questionType = 1 (Nhiều lựa chọn): tạo 4-5 đáp án, đúng ít nhất 2 đáp án.
            - questionType = 2 (Đúng/Sai): tạo đúng 2 đáp án là "Đúng" và "Sai", đánh dấu đáp án đúng phù hợp với nội dung câu hỏi.
            Nội dung câu hỏi và đáp án bằng tiếng Việt, ngắn gọn, sát với công việc thực tế của nhân viên. Đa dạng loại câu hỏi, không lặp lại nội dung.
            Chỉ trả về đúng {count} câu hỏi.
            """;

        var json = (await CallOllamaAsync(prompt, BuildQuestionSchema())).Text;
        var parsed = JsonSerializer.Deserialize<GenerateQuizQuestionsResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return NormalizeQuestionList(parsed?.QuestionList ?? []);
    }

    // ---------- Chuẩn hóa bộ câu hỏi model sinh ra ----------
    //
    // Lược đồ JSON chỉ ép được HÌNH DẠNG dữ liệu, không ép được luật "một lựa chọn thì đúng một đáp án
    // đúng". Model 7B vi phạm luật này khá thường xuyên: đo thực tế trên qwen2.5:7b, một lô 3 câu có câu
    // ghi questionType = 0 mà đánh dấu 2 đáp án đúng, và câu Đúng/Sai lại kèm 4 đáp án.
    //
    // Chỗ này không thể bỏ qua vì câu hỏi AI sinh ra được ghi THẲNG vào đề thi, không qua bước ai duyệt —
    // một câu sai luật là học viên bị chấm sai điểm mà không ai biết.
    //
    // Nguyên tắc xử lý: KHÔNG bao giờ đoán lại đáp án đúng. Đáp án model đánh dấu được giữ nguyên, chỉ
    // sửa lại LOẠI câu hỏi cho khớp với số đáp án đúng thực tế — vì loại câu hỏi suy ra được một cách chắc
    // chắn từ số đáp án đúng, còn đáp án đúng thì không suy ngược lại được. Câu nào không cứu được (không
    // có đáp án đúng nào, hoặc dưới hai đáp án) thì loại bỏ: thà trả về ít câu hơn số đã yêu cầu còn hơn
    // đưa vào đề một câu không có đáp án đúng.

    private const int QuestionTypeSingle = 0;
    private const int QuestionTypeMultiple = 1;
    private const int QuestionTypeTrueFalse = 2;

    private List<GeneratedQuestionDto> NormalizeQuestionList(List<GeneratedQuestionDto> rawList)
    {
        var resultList = new List<GeneratedQuestionDto>();
        var droppedCount = 0;
        var retypedCount = 0;

        foreach (var question in rawList)
        {
            var answerList = (question.AnswerList ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Content))
                .ToList();

            var correctCount = answerList.Count(a => a.IsCorrect);
            if (answerList.Count < 2 || correctCount == 0 || correctCount == answerList.Count)
            {
                // correctCount == answerList.Count nghĩa là mọi đáp án đều đúng — câu hỏi đó không phân
                // loại được ai hiểu bài, chấm kiểu gì cũng full điểm.
                droppedCount++;
                continue;
            }

            var expectedType = ResolveQuestionType(question.QuestionType, answerList, correctCount);
            if (expectedType != question.QuestionType) retypedCount++;

            resultList.Add(question with { QuestionType = expectedType, AnswerList = answerList });
        }

        if (droppedCount > 0 || retypedCount > 0)
            _logger.LogWarning("Chuan hoa cau hoi AI ({Model}): bo {Dropped} cau khong hop le, sua loai {Retyped} cau.",
                Model, droppedCount, retypedCount);

        return resultList;
    }

    /// <summary>Loại câu hỏi ĐÚNG với bộ đáp án thực tế, bất kể model tự khai loại nào.</summary>
    private static int ResolveQuestionType(int declaredType, List<GeneratedAnswerDto> answerList, int correctCount)
    {
        // Giữ nhãn Đúng/Sai chỉ khi bộ đáp án đúng là hai đáp án Đúng và Sai — model hay khai loại này rồi
        // vẫn kèm bốn phương án, lúc đó nó thực chất là câu một lựa chọn bình thường.
        if (declaredType == QuestionTypeTrueFalse
            && answerList.Count == 2
            && answerList.All(a => IsTrueFalseWord(a.Content))
            && correctCount == 1)
            return QuestionTypeTrueFalse;

        return correctCount == 1 ? QuestionTypeSingle : QuestionTypeMultiple;
    }

    private static bool IsTrueFalseWord(string content)
    {
        var text = content.Trim().TrimEnd('.').ToLowerInvariant();
        return text is "đúng" or "sai" or "true" or "false";
    }

    public async Task<Dictionary<string, string>> TranslateUiTextAsync(
        string sourceLanguageName, string sourceLanguageTag,
        string targetLanguageName, string targetLanguageTag,
        IReadOnlyList<UiTextToTranslateDto> itemList, CancellationToken cancellationToken = default)
    {
        if (itemList.Count == 0) return new Dictionary<string, string>();

        // Gửi kèm KHÓA DỊCH chứ không chỉ câu chữ. Khóa là thứ duy nhất nói cho model biết câu này nằm ở đâu
        // trên giao diện: "common.close" là nút Đóng, còn "quiz.close" có thể là "kết thúc bài làm" — cùng
        // một chữ "Đóng" trong tiếng Việt nhưng phần lớn ngôn ngữ đích dùng hai từ khác hẳn nhau.
        var sourceJson = new JsonArray();
        foreach (var item in itemList)
        {
            sourceJson.Add(new JsonObject
            {
                ["key"] = item.ResourceKey,
                ["text"] = item.SourceText,
            });
        }

        var prompt = $$"""
            Bạn là người dịch giao diện phần mềm, đang dịch bảng chữ của một hệ thống đào tạo nội bộ doanh
            nghiệp (e-learning: khóa học, bài học, bài kiểm tra, chứng nhận, mô phỏng 3D an toàn lao động).

            Dịch từ {{sourceLanguageName}} (mã {{sourceLanguageTag}}) sang {{targetLanguageName}} (mã {{targetLanguageTag}}).

            QUY TẮC BẮT BUỘC:
            0. NGÔN NGỮ ĐÍCH LÀ {{targetLanguageTag}}. Mọi giá trị "text" trả về phải viết bằng đúng thứ
               tiếng đó và đúng hệ chữ viết của nó. Tuyệt đối không trả lời bằng một ngôn ngữ nào khác, kể
               cả ngôn ngữ có chung mặt chữ với ngôn ngữ đích.
            1. GIỮ NGUYÊN từng chỗ giữ chỗ dạng {tên} — chép lại y hệt, không dịch chữ bên trong, không đổi
               tên, không thêm cũng không bớt cái nào. Bản dịch phải có ĐÚNG những chỗ giữ chỗ của bản nguồn.
            2. Giữ nguyên mọi thẻ HTML (<b>, <br>...), các ký hiệu · / — % và mọi con số bản nguồn có.
            3. Đây là chữ trên GIAO DIỆN: nhãn, nút bấm, tiêu đề cột, thông báo ngắn. Dịch gọn đúng văn phong
               phần mềm, không diễn giải dài ra. Bản nguồn không có dấu chấm cuối câu thì bản dịch cũng không.
            4. Trường "key" cho biết câu đó nằm ở màn hình nào — dùng nó để chọn đúng nghĩa, và CHÉP NGUYÊN
               key vào kết quả. Tuyệt đối không dịch key.
            5. Thuật ngữ đào tạo / an toàn lao động dịch theo cách nói phổ biến trong ngành.
            6. Trả về ĐỦ mọi key nhận được, không bỏ sót, không thêm key lạ.
            7. Chỉ trả về JSON đúng lược đồ, không lời dẫn, không markdown.

            Danh sách cần dịch:
            {{sourceJson.ToJsonString()}}
            """;

        var json = (await CallOllamaAsync(prompt, BuildTranslateSchema(), cancellationToken)).Text;
        var parsed = JsonSerializer.Deserialize<TranslateBatchResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        var resultByKey = new Dictionary<string, string>();
        foreach (var item in parsed?.ItemList ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Text)) continue;
            resultByKey[item.Key.Trim()] = item.Text.Trim();
        }

        return resultByKey;
    }

    private sealed record TranslateBatchResult(List<TranslateBatchItem>? ItemList);
    private sealed record TranslateBatchItem(string? Key, string? Text);

    /// <summary>Cửa sổ ngữ cảnh đủ chứa CẢ câu lệnh LẪN phần model sắp viết ra.
    ///
    /// num_ctx tính chung cho cả hai chiều. Soạn một bài học mức "chi tiết" xin tới 5120 token đầu ra, mà
    /// riêng phần tài liệu nội bộ trong câu lệnh đã cỡ nghìn rưỡi token — cộng lại vượt con số 8192 mặc
    /// định, và Ollama xử lý bằng cách lặng lẽ bỏ phần ĐẦU câu lệnh, tức đúng đoạn tài liệu công ty và bộ
    /// quy tắc bắt bám sát tài liệu. Vì vậy nới cửa sổ theo đúng nhu cầu của từng lượt gọi.
    ///
    /// Ước lượng 3 ký tự một token — tiếng Việt có dấu tốn token hơn tiếng Anh, lấy tỉ lệ này là ước
    /// lượng thừa một chút, mà thừa ngữ cảnh chỉ tốn thêm bộ nhớ còn thiếu thì mất nội dung.</summary>
    private int ResolveContextWindow(string prompt, int? maxToken)
    {
        if (maxToken is null) return _contextWindow;

        var neededToken = prompt.Length / 3 + 256 + maxToken.Value;
        return Math.Max(_contextWindow, (int)Math.Ceiling(neededToken / 1024d) * 1024);
    }

    /// <summary>Chỗ ghi lại việc model có bị cắt vì chạm trần token hay không.
    ///
    /// Phải là một đối tượng truyền vào chứ không phải giá trị trả về: hàm sinh luồng (IAsyncEnumerable)
    /// chỉ trả ra được từng mẩu chữ, không mang kèm được thông tin tổng kết ở cuối luồng.</summary>
    private sealed class StreamTail
    {
        public bool IsTruncated;
    }

    /// <summary>Gọi model qua Ollama ở chế độ stream và trả về TỪNG MẨU CHỮ ngay khi model sinh ra.
    ///
    /// Dùng chung mọi tham số với <see cref="CallOllamaAsync"/> (nhiệt độ, trần token, cửa sổ ngữ cảnh,
    /// keep_alive) để bản stream và bản thường cho ra cùng một kết quả — khác nhau đúng ở chỗ chữ về dần
    /// hay về một cục.</summary>
    private async IAsyncEnumerable<string> CallOllamaStreamAsync(string prompt, int? maxToken, StreamTail tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = true,
            ["keep_alive"] = _keepAlive,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt },
            },
            ["options"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["num_predict"] = maxToken ?? _maxContentToken,
                ["num_ctx"] = ResolveContextWindow(prompt, maxToken),
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Ollama tra ve {Status} khi soan noi dung: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Máy chủ AI nội bộ báo lỗi ({(int)response.StatusCode}). Kiểm tra Ollama tại {_baseUrl} và model {Model}.");
        }

        // Ollama trả về NDJSON: mỗi dòng là một JSON chứa một mẩu chữ.
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? piece;
            bool done;
            string? doneReason;
            try
            {
                var node = JsonNode.Parse(line);
                piece = node?["message"]?["content"]?.GetValue<string>();
                done = node?["done"]?.GetValue<bool>() ?? false;
                doneReason = node?["done_reason"]?.GetValue<string>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Bo qua dong NDJSON khong doc duoc tu Ollama: {Line}", line);
                continue;
            }

            if (!string.IsNullOrEmpty(piece)) yield return piece;

            if (done)
            {
                tail.IsTruncated = string.Equals(doneReason, "length", StringComparison.OrdinalIgnoreCase);
                if (tail.IsTruncated)
                    _logger.LogWarning("Ollama ({Model}) dung vi cham tran {Token} token - noi dung co the con thieu phan cuoi.",
                        Model, maxToken ?? _maxContentToken);
                yield break;
            }
        }
    }

    /// <summary>Model đã viết hết bài hay bị cắt vì chạm trần token — <paramref name="IsTruncated"/> lấy từ
    /// trường done_reason của Ollama ("length" nghĩa là hết token chứ không phải model tự thấy đã xong).</summary>
    private sealed record OllamaTextResult(string Text, bool IsTruncated);

    /// <summary>Gọi model nội bộ qua Ollama và trả về text thô (JSON hoặc HTML tùy responseSchema).
    ///
    /// <paramref name="maxToken"/> ghi đè trần token mặc định cho riêng lượt gọi này — soạn một bài học chi
    /// tiết cần gấp đôi số token của một lô dịch giao diện, dùng chung một con số thì hoặc bài học cụt hoặc
    /// bản dịch chờ lâu vô ích.</summary>
    private async Task<OllamaTextResult> CallOllamaAsync(string prompt, JsonObject? responseSchema,
        CancellationToken cancellationToken = default, int? maxToken = null)
    {
        var requestBody = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["keep_alive"] = _keepAlive,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt },
            },
            ["options"] = new JsonObject
            {
                // Nhiệt độ thấp: việc ở đây là soạn tài liệu đào tạo bám quy định, không phải sáng tác.
                ["temperature"] = 0.2,
                ["num_predict"] = maxToken ?? _maxContentToken,
                // Khai báo rõ cửa sổ ngữ cảnh: để mặc định thì Ollama cắt cụt phần tài liệu dài mà không báo
                // gì, và bài học sẽ thiếu đúng đoạn vừa bị cắt.
                ["num_ctx"] = ResolveContextWindow(prompt, maxToken),
            },
        };

        // Ollama nhận thẳng JSON Schema ở trường "format" và ép model sinh ra JSON khớp lược đồ. Không có
        // ràng buộc này thì model 7B hay kèm lời dẫn "Đây là kết quả:" trước JSON, và cú
        // JsonSerializer.Deserialize ở nơi gọi sẽ ném lỗi.
        if (responseSchema is not null) requestBody["format"] = responseSchema;

        using var response = await _http.PostAsync($"{_baseUrl}/api/chat",
            new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama tra ve {Status} khi soan noi dung: {Body}", (int)response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"Máy chủ AI nội bộ báo lỗi ({(int)response.StatusCode}). Kiểm tra Ollama tại {_baseUrl} và model {Model}.");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var textResult = doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
                ? content.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(textResult))
            throw new InvalidOperationException($"Máy chủ AI nội bộ không trả về nội dung: {responseBody}");

        var isTruncated = doc.RootElement.TryGetProperty("done_reason", out var doneReason)
            && string.Equals(doneReason.GetString(), "length", StringComparison.OrdinalIgnoreCase);
        if (isTruncated)
            _logger.LogWarning("Ollama ({Model}) dung vi cham tran {Token} token - noi dung co the con thieu phan cuoi.",
                Model, maxToken ?? _maxContentToken);

        return new OllamaTextResult(textResult, isTruncated);
    }

    // ---------- Dọn HTML model trả về trước khi đưa vào trình soạn thảo ----------

    /// <summary>Dọn kết quả soạn bài trước khi trả về giao diện: gỡ rào code fence, đổi markdown sang HTML
    /// nếu model lỡ viết markdown, và cắt bỏ phần dở dang khi bài bị dừng giữa chừng vì hết token.</summary>
    private static string NormalizeLessonHtml(string rawText, bool isTruncated)
    {
        var html = StripCodeFence(rawText).Trim();

        // Câu lệnh đã cấm markdown, nhưng model nội bộ vẫn thỉnh thoảng trả về "## Mục tiêu" kèm gạch đầu
        // dòng "- ". Để nguyên thì học viên đọc thấy dấu thăng và dấu sao giữa bài học.
        if (!Regex.IsMatch(html, "<(p|h[1-6]|ul|ol|li|div)[ >]", RegexOptions.IgnoreCase))
            html = ConvertMarkdownToHtml(html);

        return isTruncated ? TrimToLastCompleteBlock(html) : html;
    }

    /// <summary>Số từ THÂN BÀI — chỉ tính chữ nằm ngoài các thẻ tiêu đề.
    ///
    /// Đây là thước đo phân biệt một bài học thật với một dàn ý: dàn ý cũng có đủ tiêu đề như bài học thật,
    /// khác nhau ở chỗ dưới mỗi tiêu đề có chữ hay không.</summary>
    private static int CountBodyWord(string html)
    {
        var withoutHeading = Regex.Replace(html, "<h[1-6][^>]*>.*?</h[1-6]>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(withoutHeading, "<[^>]+>", " "));

        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Cắt bỏ khối cuối cùng còn dở khi model dừng vì hết token, rồi đóng nốt các thẻ danh sách còn
    /// mở. Bỏ nguyên câu cụt còn hơn để học viên đọc phải một câu đứt giữa chừng kèm thẻ HTML hỏng.</summary>
    private static string TrimToLastCompleteBlock(string html)
    {
        var closeTagList = Regex.Matches(html, "</(p|ul|ol|h3|h4|li)>", RegexOptions.IgnoreCase);
        if (closeTagList.Count == 0) return html;

        var lastClose = closeTagList[^1];
        var trimmed = html[..(lastClose.Index + lastClose.Length)];

        // Cắt ngay sau một </li> thì thẻ <ul>/<ol> bao ngoài vẫn đang mở. Đếm số thẻ mở còn thiếu thẻ đóng
        // rồi bù vào cuối — không xử lý danh sách lồng nhau nhiều tầng vì bài học AI soạn không có kiểu đó.
        var builder = new StringBuilder(trimmed);
        foreach (var tagName in new[] { "ul", "ol" })
        {
            var openCount = Regex.Matches(trimmed, $"<{tagName}[ >]", RegexOptions.IgnoreCase).Count;
            var closeCount = Regex.Matches(trimmed, $"</{tagName}>", RegexOptions.IgnoreCase).Count;
            for (var i = closeCount; i < openCount; i++) builder.Append($"</{tagName}>");
        }

        return builder.ToString();
    }

    /// <summary>Đổi markdown thô sang HTML tối giản — chỉ dùng khi model trả về markdown thay vì HTML như
    /// đã dặn. Xử lý đúng ba thứ model hay dùng: tiêu đề dấu thăng, gạch đầu dòng, và chữ đậm hai dấu sao.</summary>
    private static string ConvertMarkdownToHtml(string markdown)
    {
        var builder = new StringBuilder();
        var isInList = false;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            var isBullet = line.StartsWith("- ") || line.StartsWith("* ") || Regex.IsMatch(line, @"^\d+[.)]\s");

            if (isInList && !isBullet) { builder.AppendLine("</ul>"); isInList = false; }
            if (line.Length == 0) continue;

            var text = Regex.Replace(line, @"\*\*(.+?)\*\*", "<b>$1</b>");

            if (text.StartsWith("#"))
            {
                builder.AppendLine($"<h3>{text.TrimStart('#').Trim()}</h3>");
                continue;
            }

            if (isBullet)
            {
                if (!isInList) { builder.AppendLine("<ul>"); isInList = true; }
                builder.AppendLine($"<li>{Regex.Replace(text, @"^([-*]|\d+[.)])\s*", "")}</li>");
                continue;
            }

            builder.AppendLine($"<p>{text}</p>");
        }

        if (isInList) builder.AppendLine("</ul>");
        return builder.ToString().Trim();
    }

    /// <summary>Gỡ rào code fence kiểu markdown nếu model vẫn bọc kết quả vào đó.
    ///
    /// Câu lệnh đã dặn không dùng code fence, nhưng model nội bộ cỡ 7B thỉnh thoảng vẫn bọc — để nguyên thì
    /// ba dấu huyền hiện thành chữ ngay giữa bài học của học viên. Chỉ gỡ khi rào bao TRỌN cả chuỗi: một bài
    /// học có ví dụ mã lệnh ở giữa thì rào bên trong đó là nội dung thật, không được đụng tới.</summary>
    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        var match = Regex.Match(trimmed, @"^```[a-zA-Z]*\s*\n(.*?)\n?```$", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : trimmed;
    }

    /// <summary>Lược đồ JSON Schema cho bộ câu hỏi — Ollama dùng nó để ép định dạng đầu ra.</summary>
    private static JsonObject BuildQuestionSchema()
    {
        const string schemaJson = """
            {
                "type": "object",
                "properties": {
                    "questionList": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "content": { "type": "string" },
                                "questionType": { "type": "integer" },
                                "explanation": { "type": "string" },
                                "answerList": {
                                    "type": "array",
                                    "items": {
                                        "type": "object",
                                        "properties": {
                                            "content": { "type": "string" },
                                            "isCorrect": { "type": "boolean" }
                                        },
                                        "required": ["content", "isCorrect"]
                                    }
                                }
                            },
                            "required": ["content", "questionType", "answerList"]
                        }
                    }
                },
                "required": ["questionList"]
            }
            """;

        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    private static JsonObject BuildTranslateSchema()
    {
        const string schemaJson = """
            {
                "type": "object",
                "properties": {
                    "itemList": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "key": { "type": "string" },
                                "text": { "type": "string" }
                            },
                            "required": ["key", "text"]
                        }
                    }
                },
                "required": ["itemList"]
            }
            """;

        return JsonNode.Parse(schemaJson)!.AsObject();
    }
}
