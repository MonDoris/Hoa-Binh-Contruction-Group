// File: Application/Features/AiDocuments/AiDocumentService.cs
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiDocuments;

/// <summary>Quản trị kho tri thức AI: tải tài liệu PDF/Word lên, hệ thống bóc tách và sinh vector nhúng
/// (bằng Ollama chạy nội bộ) để dùng cho tìm kiếm ngữ nghĩa / hỏi đáp tài liệu đào tạo.
/// Chỉ Admin hệ thống được dùng — đây là dữ liệu nền dùng chung cho toàn công ty.</summary>
public class AiDocumentService : IAiDocumentService
{
    /// <summary>Thư mục lưu file gốc trong wwwroot/uploads.</summary>
    private const string UploadSubFolder = "ai-document";

    private static readonly string[] SupportedExtensionList = [".pdf", ".docx", ".txt", ".md"];

    private readonly IAppDbContext _db;
    private readonly IDocumentProcessingService _documentProcessing;
    private readonly ICourseKnowledgeService _courseKnowledge;
    private readonly IEmbeddingService _embedding;
    private readonly IAiChatService _chat;
    private readonly IFileService _fileService;
    private readonly IAppPaths _env;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiDocumentService> _logger;

    public AiDocumentService(IAppDbContext db, IDocumentProcessingService documentProcessing, ICourseKnowledgeService courseKnowledge, IEmbeddingService embedding, IAiChatService chat, IFileService fileService, IAppPaths env, IMemoryCache cache, ILogger<AiDocumentService> logger)
    {
        _db = db;
        _documentProcessing = documentProcessing;
        _courseKnowledge = courseKnowledge;
        _embedding = embedding;
        _chat = chat;
        _fileService = fileService;
        _env = env;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> GetStatus(CancellationToken cancellationToken)
        => Result.Success(await _embedding.CheckStatusAsync(_chat.Model, cancellationToken));

    public async Task<Result> GetList(int? courseId, CancellationToken cancellationToken)
    {
        // Chỉ liệt kê TÀI LIỆU TẢI LÊN. Nội dung khóa học do máy tự đồng bộ cũng nằm trong bảng này, nhưng
        // nó không phải thứ quản trị viên thêm/xóa bằng tay ở màn hình này — kê chung vào đây thì danh sách
        // vài chục tài liệu thật bị hàng trăm dòng bài học nhấn chìm, và bấm xóa một dòng như thế cũng vô
        // ích vì lượt đồng bộ kế tiếp nạp lại ngay. Phần đó có bảng riêng ở "kiến thức khóa học" bên dưới.
        var query = _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceKind == KnowledgeSourceKind.UploadedDocument);
        if (courseId is not null) query = query.Where(x => x.CourseId == courseId);

        var groupList = await query
            .GroupBy(x => new { x.SourceFileName, x.CourseId, x.EmbeddingModel })
            .Select(g => new
            {
                g.Key.SourceFileName,
                g.Key.CourseId,
                g.Key.EmbeddingModel,
                ChunkCount = g.Count(),
                CreatedAt = g.Max(x => x.CreatedAt),
                SourceFileUrl = g.Min(x => x.SourceFileUrl)
            })
            .ToListAsync(cancellationToken);

        var courseIdList = groupList.Where(x => x.CourseId != null).Select(x => x.CourseId!.Value).Distinct().ToList();
        var courseTitleMap = await _db.Course.AsNoTracking()
            .Where(x => courseIdList.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        return Result.Success(groupList
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AiDocumentListItemDto(
                x.SourceFileName, x.SourceFileUrl, x.ChunkCount, x.CourseId,
                x.CourseId is not null && courseTitleMap.TryGetValue(x.CourseId.Value, out var title) ? title : null,
                x.EmbeddingModel, x.CreatedAt)));
    }

    public async Task<Result> GetChunkList(string sourceFileName, int? courseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return Result.Invalid(new MessageResponse("Thiếu tên tài liệu."));

        var chunkList = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceFileName == sourceFileName && (courseId == null || x.CourseId == courseId))
            .OrderBy(x => x.ChunkIndex)
            .Select(x => new DocumentChunkMatchDto(
                x.Id, x.SourceFileName, x.ChunkIndex, x.TextContent, 0, x.CourseId, x.LessonId))
            .ToListAsync(cancellationToken);

        return Result.Success(chunkList);
    }

    public async Task<Result> Upload(FileUpload file, int? courseId, int? lessonId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn tài liệu."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!SupportedExtensionList.Contains(extension))
            return Result.Invalid(new MessageResponse(
                $"Định dạng {extension} chưa hỗ trợ. Chỉ nhận: {string.Join(", ", SupportedExtensionList)} " +
                "(file .doc cũ cần mở bằng Word và lưu lại thành .docx)."));

        if (courseId is not null && !await _db.Course.AnyAsync(x => x.Id == courseId, cancellationToken))
            return Result.Invalid(new MessageResponse("Khóa học được chọn không tồn tại."));

        // Ném lỗi sớm và rõ ràng thay vì để người dùng chờ hết thời gian tải file rồi mới báo Ollama không chạy.
        var status = await _embedding.CheckStatusAsync(cancellationToken: cancellationToken);
        if (!status.IsReady) return Result.ServiceUnavailable(new MessageResponse(status.Message));

        // File gốc của bản cũ cùng tên — xóa sau khi nạp bản mới thành công, tránh rác trong wwwroot/uploads.
        var previousUrl = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceFileName == file.FileName && (courseId == null || x.CourseId == courseId))
            .Select(x => x.SourceFileUrl)
            .FirstOrDefaultAsync(cancellationToken);

        UploadResultDto saved;
        try
        {
            saved = await _fileService.SaveAsync(file, UploadSubFolder);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(new MessageResponse(ex.Message));
        }

        var physicalPath = Path.Combine(
            string.IsNullOrEmpty(_env.WebRootPath) ? Path.Combine(_env.ContentRootPath, "wwwroot") : _env.WebRootPath,
            saved.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        try
        {
            var result = await _documentProcessing.ProcessAndSaveDocumentAsync(
                physicalPath,
                courseId: courseId,
                lessonId: lessonId,
                sourceFileUrl: saved.Url,
                displayFileName: file.FileName,
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(previousUrl) && previousUrl != saved.Url)
                _fileService.Delete(previousUrl);

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            // Nạp hỏng thì không giữ lại file vừa tải lên — nếu giữ, wwwroot đầy file không đoạn nào trỏ tới.
            _fileService.Delete(saved.Url);
            _logger.LogError(ex, "Nap tai lieu AI that bai: {FileName}", file.FileName);

            // NotSupportedException/InvalidOperationException là lỗi do chính file người dùng chọn (sai định dạng,
            // không đọc được nội dung) nên nói thẳng cho họ sửa. Còn lại là lỗi hạ tầng — chi tiết đã vào log ở trên,
            // trả về câu chung chung để không lộ địa chỉ máy chủ AI và tên model ra ngoài.
            return ex is NotSupportedException or InvalidOperationException
                ? Result.Invalid(new MessageResponse(ex.Message))
                : Result.ExternalServiceFailed(new MessageResponse("Không nạp được tài liệu. Vui lòng liên hệ quản trị hệ thống."));
        }
    }

    public async Task<Result> Search(AiDocumentSearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Result.Invalid(new MessageResponse("Vui lòng nhập câu hỏi cần tìm."));

        try
        {
            return Result.Success(await _documentProcessing.SearchAsync(
                request.Query, request.TopCount, request.CourseId, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tim kiem ngu nghia tren tai lieu AI that bai: {Query}", request.Query);
            return Result.ExternalServiceFailed(new MessageResponse("Không tìm kiếm được. Vui lòng liên hệ quản trị hệ thống."));
        }
    }

    public async Task Ask(AiDocumentAskRequest request, IEventStream events, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await events.SendAsync("error", new MessageResponse("Vui lòng nhập câu hỏi."), cancellationToken);
            return;
        }

        try
        {
            var foundList = await _documentProcessing.GetAnswerContextAsync(
                request.Question.Trim(), topCount: 4, request.CourseId, cancellationToken: cancellationToken);

            // Cắt theo đúng ngân sách câu lệnh TRƯỚC khi lập bảng nguồn: danh sách sau khi cắt vừa là thứ
            // gửi cho model, vừa là bảng "AI đã dựa vào các đoạn sau" hiện cho người hỏi đối chiếu.
            var contextList = _chat.TrimToPromptBudget(foundList);

            var sourceList = contextList
                .Select((x, i) => new AiAnswerSourceDto(i + 1, x.SourceFileName, x.ChunkIndex, x.TextContent, x.Score))
                .ToList();

            await events.SendAsync("sources", sourceList, cancellationToken);

            if (contextList.Count == 0)
            {
                await events.SendAsync("token", new { t = IAiChatService.NoInformationAnswer }, cancellationToken);
                await events.SendAsync("done", new { }, cancellationToken);
                return;
            }

            // Câu hỏi đã trả lời rồi thì trả về ngay thay vì bắt model đọc lại từ đầu. Trong đào tạo nội bộ
            // đây là chuyện thường xuyên: cả phòng cùng học một khóa thì cùng vướng đúng mấy chỗ như nhau,
            // và mỗi lượt hỏi lại tốn của model hàng chục giây cho một câu trả lời y hệt lần trước.
            var cacheKey = BuildAnswerCacheKey(request.Question, request.CourseId, contextList);
            if (_cache.TryGetValue<string>(cacheKey, out var cachedAnswer) && cachedAnswer is { Length: > 0 })
            {
                await events.SendAsync("token", new { t = cachedAnswer }, cancellationToken);
                await events.SendAsync("done", new { }, cancellationToken);
                return;
            }

            var answerBuilder = new StringBuilder();
            await foreach (var piece in _chat.StreamAnswerAsync(request.Question.Trim(), contextList, cancellationToken: cancellationToken))
            {
                answerBuilder.Append(piece);
                await events.SendAsync("token", new { t = piece }, cancellationToken);
            }

            // Chỉ lưu khi model đã viết XONG. Người hỏi bỏ ngang giữa chừng thì phần chữ dở dang không phải
            // câu trả lời, mà lưu vào rồi thì mọi người hỏi lại sau đó đều nhận đúng mẩu cụt ấy.
            if (answerBuilder.Length > 0 && !cancellationToken.IsCancellationRequested)
                _cache.Set(cacheKey, answerBuilder.ToString(), AnswerCacheDuration);

            await events.SendAsync("done", new { }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Người dùng rời trang / bấm hủy giữa chừng — không phải lỗi, không cần ghi log.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hoi dap tai lieu AI that bai.");
            await events.SendAsync("error", new MessageResponse("Không trả lời được. Vui lòng liên hệ quản trị hệ thống."), CancellationToken.None);
        }
    }

    /// <summary>Câu trả lời nằm trong bộ nhớ đệm bao lâu. Đặt theo ngày chứ không theo giờ vì kho tài liệu
    /// đổi rất thưa, mà khóa đệm đã gồm mã các đoạn tài liệu: nạp lại tài liệu là sinh đoạn mới với mã mới,
    /// nên bản đệm cũ tự hết hiệu lực chứ không cần chờ hết hạn.</summary>
    private static readonly TimeSpan AnswerCacheDuration = TimeSpan.FromHours(24);

    /// <summary>Khóa đệm gồm: câu hỏi (bỏ qua khác biệt hoa thường và khoảng trắng thừa), khóa học đang hỏi,
    /// model trả lời, và mã CÁC ĐOẠN TÀI LIỆU đã đưa cho model.
    ///
    /// Có mã các đoạn trong khóa mới đúng: cùng một câu hỏi nhưng kho tri thức đã được nạp thêm tài liệu thì
    /// các đoạn tìm được đã khác, và câu trả lời cũ có thể đã lỗi thời so với quy định mới nạp vào.</summary>
    private string BuildAnswerCacheKey(string question, int? courseId, IReadOnlyList<DocumentChunkMatchDto> contextList)
    {
        var normalizedQuestion = string.Join(' ',
            question.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return $"ai-answer|{_chat.Model}|{courseId}|{string.Join(',', contextList.Select(x => x.Id))}|{normalizedQuestion}";
    }

    public async Task<Result> GetCourseKnowledge(CancellationToken cancellationToken)
        => Result.Success(await _courseKnowledge.GetStatusAsync(cancellationToken));

    public async Task<Result> SyncCourseKnowledge(bool isForced, int? courseId, CancellationToken cancellationToken)
    {
        try
        {
            return Result.Success(await _courseKnowledge.SyncAsync(isForced, courseId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            // Máy chủ AI chưa dùng được — câu chữ đã do tầng dịch vụ soạn sẵn cho người dùng cuối.
            return Result.ServiceUnavailable(new MessageResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dong bo kien thuc khoa hoc that bai.");
            return Result.ExternalServiceFailed(new MessageResponse("Không học được nội dung khóa học. Vui lòng liên hệ quản trị hệ thống."));
        }
    }

    public async Task<Result> Delete(string sourceFileName, int? courseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return Result.Invalid(new MessageResponse("Thiếu tên tài liệu cần xóa."));

        var fileUrlList = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceKind == KnowledgeSourceKind.UploadedDocument)
            .Where(x => x.SourceFileName == sourceFileName && (courseId == null || x.CourseId == courseId))
            .Select(x => x.SourceFileUrl)
            .Distinct()
            .ToListAsync(cancellationToken);

        var removedCount = await _documentProcessing.DeleteBySourceAsync(sourceFileName, courseId, cancellationToken);
        if (removedCount == 0) return Result.NotFound(new MessageResponse("Không tìm thấy tài liệu này trong kho tri thức."));

        foreach (var url in fileUrlList.Where(x => !string.IsNullOrWhiteSpace(x)))
            _fileService.Delete(url!);

        return Result.Success(new MessageResponse($"Đã xóa tài liệu \"{sourceFileName}\" ({removedCount} đoạn)."));
    }
}
