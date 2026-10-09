// File: Application/Features/Ai/AiService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Ai;

/// <summary>Dùng AI hỗ trợ tạo nội dung bài học và câu hỏi kiểm tra cho đào tạo nội bộ.
///
/// Nội dung bài học có thể được sinh theo hai chế độ:
///   - Kiến thức chung: model tự viết theo hiểu biết của nó (nhanh, nhưng không nói đúng quy định công ty).
///   - BÁM SÁT TÀI LIỆU CÔNG TY: tra kho tri thức "Tài liệu AI" (RAG) lấy các đoạn tài liệu nội bộ liên
///     quan nhất rồi mới gọi model, và bắt model chỉ viết dựa trên các đoạn đó — xem GenerateLessonContent.
/// </summary>
public class AiService : IAiService
{
    /// <summary>Số đoạn tài liệu nội bộ gửi kèm câu lệnh. Nhiều hơn quiz/hỏi đáp (4-5 đoạn) vì một bài học
    /// phải bao quát cả một chủ đề chứ không trả lời đúng một câu hỏi; nhưng vẫn có trần để câu lệnh không
    /// phình tới mức model đọc lướt và bỏ sót chính phần quan trọng.</summary>
    private const int CompanyContextChunkCount = 10;

    /// <summary>Dưới ngần này từ thân bài thì coi như AI mới ra được dàn ý chứ chưa phải bài học — giao diện
    /// sẽ cảnh báo người soạn đọc lại thay vì lưu thẳng. Lấy thấp hơn ngưỡng viết-lại-lần-hai bên dịch vụ AI:
    /// tới đây thì model đã được cho hai lượt, còn mỏng nữa là chuyện phải báo cho người dùng.</summary>
    private const int ThinLessonWordCount = 260;

    private readonly IAiContentService _ai;
    private readonly IDocumentProcessingService _documentProcessing;
    private readonly IEmbeddingService _embedding;
    private readonly IAppDbContext _db;
    private readonly ILogger<AiService> _logger;

    /// <summary>Câu báo chung khi gọi máy chủ AI thất bại. Chi tiết (địa chỉ máy chủ AI, tên model, lời của
    /// thư viện HTTP) chỉ vào nhật ký — người dùng cuối không cần và cũng không nên thấy.</summary>
    private const string AiFailureMessage =
        "Máy chủ AI đang không phản hồi. Vui lòng thử lại sau ít phút; nếu vẫn lỗi, báo quản trị hệ thống.";

    public AiService(IAiContentService ai, IDocumentProcessingService documentProcessing, IEmbeddingService embedding, IAppDbContext db, ILogger<AiService> logger)
    {
        _ai = ai;
        _documentProcessing = documentProcessing;
        _embedding = embedding;
        _db = db;
        _logger = logger;
    }

    public async Task<Result> GetKnowledgeScope(int? courseId, CancellationToken cancellationToken)
    {
        // Cùng phạm vi lọc với DocumentProcessingService.SearchAsync: tài liệu của đúng khóa học này
        // CỘNG tài liệu dùng chung toàn công ty (CourseId = null).
        var chunkQuery = _db.DocumentChunk.AsNoTracking()
            .Where(x => courseId == null || x.CourseId == courseId || x.CourseId == null);

        var fileNameList = await chunkQuery
            .Select(x => x.SourceFileName).Distinct().OrderBy(x => x)
            .ToListAsync(cancellationToken);
        var chunkCount = await chunkQuery.CountAsync(cancellationToken);

        var status = await _embedding.CheckStatusAsync(cancellationToken: cancellationToken);

        var message = fileNameList.Count == 0
            ? "Kho tri thức chưa có tài liệu nào dùng được cho khóa học này. Nhờ quản trị viên nạp tài liệu ở màn hình \"Tài liệu AI\"."
            : status.IsReady
                ? $"Sẵn sàng: {fileNameList.Count} tài liệu ({chunkCount} đoạn)."
                : status.Message;

        return Result.Success(new AiKnowledgeScopeDto(fileNameList.Count, chunkCount, fileNameList, status.IsReady, message));
    }

    /// <summary>Kết quả bước chuẩn bị chung của hai endpoint soạn bài (bản chờ trọn gói và bản stream):
    /// hoặc <paramref name="ErrorMessage"/> có chữ (không đi tiếp được), hoặc danh sách đoạn tài liệu đã sẵn sàng.
    /// <paramref name="IsServerError"/> phân biệt lỗi phía máy chủ AI (502) với lỗi do người dùng nhập (400).</summary>
    private sealed record LessonContextPreparation(
        string? ErrorMessage, bool IsServerError, List<DocumentChunkMatchDto> CompanyContextList);

    /// <summary>Kiểm tra đầu vào rồi tra kho tri thức lấy các đoạn tài liệu công ty cho một lượt soạn bài.
    /// Tách riêng ra vì cả endpoint chờ trọn gói lẫn endpoint stream đều phải làm y hệt các bước này — làm
    /// lệch nhau ở một trong hai chỗ là hai đường đi cho ra hai bài học khác nhau từ cùng một yêu cầu.</summary>
    private async Task<LessonContextPreparation> PrepareLessonContextAsync(
        GenerateLessonContentRequest request, CancellationToken cancellationToken)
    {
        if (!_ai.IsConfigured)
            return new LessonContextPreparation(
                "Chưa cấu hình máy chủ AI nội bộ (mục Ollama trong appsettings.json). Vui lòng liên hệ quản trị viên hệ thống.",
                false, []);

        if (string.IsNullOrWhiteSpace(request.LessonTitle))
            return new LessonContextPreparation("Vui lòng nhập tiêu đề bài học trước khi dùng AI.", false, []);

        if (!request.UseCompanyDocument) return new LessonContextPreparation(null, false, []);

        var lessonTitle = request.LessonTitle.Trim();
        var topic = request.Topic?.Trim();

        // Câu truy vấn ghép cả tiêu đề lẫn chủ đề: tiêu đề bài học thường quá ngắn ("Bước 3: Nghiệm thu")
        // để một mình nó tìm trúng đoạn tài liệu, còn chủ đề thì mang đúng từ khóa nghiệp vụ cần tra.
        var query = string.IsNullOrWhiteSpace(topic) ? lessonTitle : $"{lessonTitle}. {topic}";

        List<DocumentChunkMatchDto> companyContextList;
        try
        {
            companyContextList = await _documentProcessing.GetAnswerContextAsync(
                query, CompanyContextChunkCount, request.CourseId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tra kho tri thuc de soan bai hoc that bai: {Query}", query);
            return new LessonContextPreparation(
                "Không tra cứu được kho tài liệu công ty (máy chủ AI nội bộ chưa sẵn sàng). " +
                "Bỏ chọn \"Bám sát tài liệu công ty\" để tạo nội dung theo kiến thức chung, hoặc liên hệ quản trị hệ thống.",
                true, []);
        }

        companyContextList = TrimToPromptBudget(companyContextList);

        // Không tìm thấy đoạn nào mà vẫn tạo tiếp thì người soạn bài nhận về một bài học kiến thức chung
        // NHƯNG lại tin rằng nó bám quy định công ty — sai lệch nguy hiểm hơn hẳn việc báo lỗi ở đây.
        if (companyContextList.Count == 0)
            return new LessonContextPreparation(
                "Kho tài liệu công ty chưa có nội dung nào liên quan tới bài học này. " +
                "Hãy nạp tài liệu tương ứng ở màn hình \"Tài liệu AI\", đổi lại chủ đề cho sát tài liệu đã có, " +
                "hoặc bỏ chọn \"Bám sát tài liệu công ty\" để AI viết theo kiến thức chung.",
                false, []);

        return new LessonContextPreparation(null, false, companyContextList);
    }

    /// <summary>Lập bảng "AI đã dựa vào các đoạn sau" từ đúng danh sách đã gửi cho model.</summary>
    private static List<AiAnswerSourceDto> BuildSourceList(IReadOnlyList<DocumentChunkMatchDto> companyContextList)
        => [.. companyContextList.Select((x, i) => new AiAnswerSourceDto(i + 1, x.SourceFileName, x.ChunkIndex, x.TextContent, x.Score))];

    public async Task<Result> GenerateLessonContent(GenerateLessonContentRequest request, CancellationToken cancellationToken)
    {
        var preparation = await PrepareLessonContextAsync(request, cancellationToken);
        if (preparation.ErrorMessage is { Length: > 0 } errorMessage)
            return preparation.IsServerError
                ? Result.ExternalServiceFailed(new MessageResponse(errorMessage))
                : Result.Invalid(new MessageResponse(errorMessage));

        var lessonTitle = request.LessonTitle.Trim();
        var topic = request.Topic?.Trim();
        var companyContextList = preparation.CompanyContextList;

        try
        {
            var result = await _ai.GenerateLessonContentAsync(
                request.CourseTitle, lessonTitle, topic, request.Instructions?.Trim(), companyContextList,
                request.DetailLevel, cancellationToken);

            var sourceList = BuildSourceList(companyContextList);

            // Bài mỏng vẫn TRẢ VỀ chứ không báo lỗi: người soạn chờ vài phút, cầm một bản nháp sơ sài để sửa
            // tay vẫn hơn là nhận thông báo lỗi và mất trắng. Nhưng phải kèm cờ IsThin để giao diện cảnh báo,
            // không thì bài dàn ý bị lưu thẳng vào khóa học mà không ai để ý.
            var isThin = result.BodyWordCount < ThinLessonWordCount;
            if (isThin)
                _logger.LogWarning("Bai hoc AI cho '{Title}' chi dat {Word} tu than bai.", lessonTitle, result.BodyWordCount);

            return Result.Success(new GenerateLessonContentResponse(
                result.Html, sourceList, result.BodyWordCount, isThin, result.IsTruncated));
        }
        catch (Exception ex)
        {
            // Không ghép lời ngoại lệ vào câu trả về: nó mang theo địa chỉ máy chủ AI nội bộ và tên model
            // đang chạy. Chi tiết vào nhật ký cho người quản trị.
            _logger.LogError(ex, "Goi may chu AI that bai khi soan noi dung bai hoc");
            return Result.ExternalServiceFailed(new MessageResponse(AiFailureMessage));
        }
    }

    public async Task GenerateLessonContentStream(GenerateLessonContentRequest request, IEventStream events, CancellationToken cancellationToken)
    {
        var preparation = await PrepareLessonContextAsync(request, cancellationToken);
        if (preparation.ErrorMessage is { Length: > 0 } errorMessage)
        {
            await events.SendAsync("error", new MessageResponse(errorMessage), cancellationToken);
            return;
        }

        var lessonTitle = request.LessonTitle.Trim();
        var companyContextList = preparation.CompanyContextList;

        // Gửi bảng nguồn TRƯỚC khi model chạy: người soạn thấy ngay AI sắp bám vào tài liệu nào, và nếu sai
        // tài liệu thì hủy luôn thay vì chờ hết mấy phút mới phát hiện.
        await events.SendAsync("sources", BuildSourceList(companyContextList), cancellationToken);

        try
        {
            await foreach (var piece in _ai.StreamLessonContentAsync(
                request.CourseTitle, lessonTitle, request.Topic?.Trim(), request.Instructions?.Trim(),
                companyContextList, request.DetailLevel, cancellationToken))
            {
                if (piece.IsRewriteStart)
                {
                    await events.SendAsync("rewrite", new { }, cancellationToken);
                    continue;
                }

                if (piece.Token is { Length: > 0 } token)
                {
                    await events.SendAsync("token", new { t = token }, cancellationToken);
                    continue;
                }

                if (piece.Result is not { } result) continue;

                var isThin = result.BodyWordCount < ThinLessonWordCount;
                if (isThin)
                    _logger.LogWarning("Bai hoc AI cho '{Title}' chi dat {Word} tu than bai.", lessonTitle, result.BodyWordCount);

                await events.SendAsync("result", new GenerateLessonContentResponse(
                    result.Html, BuildSourceList(companyContextList), result.BodyWordCount, isThin, result.IsTruncated),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Người soạn đóng hộp thoại / rời trang giữa chừng — không phải lỗi, không cần ghi log.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Soan noi dung bai hoc bang AI that bai (stream).");
            await events.SendAsync("error", new MessageResponse(AiFailureMessage), CancellationToken.None);
        }
    }

    /// <summary>Trần số ký tự tài liệu nội bộ được đưa vào một lượt soạn bài.
    ///
    /// Đây là chỗ đắt nhất của cả tính năng khi chạy model nội bộ: đo trên máy CPU thuần, mỗi khoảng 1000
    /// ký tự tài liệu tốn thêm chừng 15 giây chỉ để model ĐỌC, trước khi viết được chữ đầu tiên. Lấy trọn
    /// cả 10 đoạn (khoảng 5000 ký tự) thì riêng phần đọc đã hơn một phút rưỡi.
    ///
    /// Cắt ở đây chứ không cắt trong dịch vụ AI là có chủ đích: danh sách còn lại sau khi cắt vừa là thứ
    /// gửi cho model, vừa là bảng "AI đã dựa vào các đoạn sau" hiện cho người soạn bài đối chiếu. Hai thứ
    /// đó phải là MỘT, nếu không bảng nguồn sẽ kê ra những đoạn model chưa từng đọc.</summary>
    private const int MaxContextCharacter = 4000;

    /// <summary>Giữ lại các đoạn đầu danh sách cho tới khi chạm trần ký tự.
    ///
    /// Dừng hẳn ở đoạn đầu tiên vượt trần chứ không bỏ qua nó để nhặt tiếp đoạn ngắn phía sau: danh sách
    /// đã xếp theo độ liên quan giảm dần, nhảy cóc là đánh đổi đoạn sát nghĩa lấy đoạn xa nghĩa hơn.</summary>
    private static List<DocumentChunkMatchDto> TrimToPromptBudget(List<DocumentChunkMatchDto> contextList)
    {
        var resultList = new List<DocumentChunkMatchDto>();
        var usedCharacter = 0;

        foreach (var chunk in contextList)
        {
            var length = chunk.TextContent.Trim().Length;
            if (resultList.Count > 0 && usedCharacter + length > MaxContextCharacter) break;

            resultList.Add(chunk);
            usedCharacter += length;
        }

        return resultList;
    }

    public async Task<Result> GenerateQuizQuestions(GenerateQuizQuestionsRequest request)
    {
        if (!_ai.IsConfigured)
            return Result.Invalid(new MessageResponse("Chưa cấu hình máy chủ AI nội bộ (mục Ollama trong appsettings.json). Vui lòng liên hệ quản trị viên hệ thống."));

        if (string.IsNullOrWhiteSpace(request.Topic))
            return Result.Invalid(new MessageResponse("Vui lòng nhập chủ đề câu hỏi."));

        var count = Math.Clamp(request.Count <= 0 ? 5 : request.Count, 1, 20);

        try
        {
            var questionList = await _ai.GenerateQuizQuestionsAsync(
                request.CourseTitle, request.Topic.Trim(), count, request.Difficulty?.Trim());
            return Result.Success(new GenerateQuizQuestionsResponse(questionList));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Goi may chu AI that bai khi sinh cau hoi kiem tra");
            return Result.ExternalServiceFailed(new MessageResponse(AiFailureMessage));
        }
    }
}
