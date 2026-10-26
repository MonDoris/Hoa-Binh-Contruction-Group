// File: Application/Services/DocumentProcessingService.cs
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Services;

/// <summary>Nạp tài liệu đào tạo (PDF/Word) vào kho tri thức: bóc tách văn bản -> cắt đoạn -> sinh vector nhúng
/// bằng Ollama chạy nội bộ -> lưu database. Dùng cho tìm kiếm ngữ nghĩa và hỏi đáp tài liệu.
///
/// Chuyển từ Infrastructure về Application khi tách lớp: phần lớn lớp này là nghiệp vụ kho tri thức (cắt đoạn,
/// thay bản cũ khi nạp lại, hàng rào quyền đọc theo khóa học, bù đoạn liền kề cho ngữ cảnh). Chỉ việc bóc chữ từ
/// PDF/Word là hạ tầng, đi qua <see cref="IDocumentTextExtractor"/>; sinh vector đi qua <see cref="IEmbeddingService"/>.</summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    /// <summary>Số đoạn gửi cho Ollama trong một lần gọi — gửi cả nghìn đoạn một lượt dễ làm Ollama hết bộ nhớ.</summary>
    private const int EmbeddingBatchSize = 16;

    private readonly IAppDbContext _db;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly ChunkVectorIndex _index;
    private readonly IMemoryCache _cache;

    public DocumentProcessingService(IAppDbContext db, IDocumentTextExtractor textExtractor, IEmbeddingService embedding,
        ILogger<DocumentProcessingService> logger, ChunkVectorIndex index, IMemoryCache cache)
    {
        _index = index;
        _cache = cache;
        _db = db;
        _textExtractor = textExtractor;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<DocumentIngestResultDto> ProcessAndSaveDocumentAsync(
        string filePath, string? fileType = null, int? courseId = null, int? lessonId = null,
        string? sourceFileUrl = null, string? displayFileName = null, CancellationToken cancellationToken = default)
    {
        // 1. Phân loại định dạng file — tự suy ra từ phần mở rộng nếu không được truyền vào.
        var extension = (fileType ?? Path.GetExtension(filePath)).Trim().TrimStart('.').ToLowerInvariant();
        var extractedText = await _textExtractor.ExtractTextAsync(filePath, extension, cancellationToken);

        extractedText = NormalizeWhitespace(extractedText);
        if (string.IsNullOrWhiteSpace(extractedText))
            throw new InvalidOperationException(
                "Không bóc tách được chữ nào từ tài liệu. File PDF dạng ảnh scan cần OCR trước khi nạp.");

        // File tải lên được FileService lưu thành tên GUID, nên tên hiển thị/khóa nhận dạng tài liệu phải lấy
        // từ tên gốc người dùng chọn — nếu dùng tên GUID thì mỗi lần tải lên lại là một tài liệu "mới" khác nhau.
        var fileName = string.IsNullOrWhiteSpace(displayFileName) ? Path.GetFileName(filePath) : displayFileName.Trim();

        // 2. Cắt nhỏ văn bản theo ranh giới câu, có gối đầu giữa các đoạn.
        var textChunkList = TextChunker.SplitIntoChunks(extractedText);

        // 3. Xóa dữ liệu cũ của chính file này (trường hợp nạp lại bản cập nhật) rồi lấy vector và lưu.
        var removedCount = await DeleteBySourceAsync(fileName, courseId, cancellationToken);

        var chunkIndex = 0;
        foreach (var batch in textChunkList.Chunk(EmbeddingBatchSize))
        {
            var vectorList = await _embedding.GetEmbeddingListAsync(batch, cancellationToken);
            if (vectorList.Count != batch.Length)
                throw new InvalidOperationException(
                    $"Ollama trả về {vectorList.Count} vector nhưng đã gửi {batch.Length} đoạn.");

            for (int i = 0; i < batch.Length; i++)
            {
                _db.DocumentChunk.Add(new DocumentChunk
                {
                    SourceKind = KnowledgeSourceKind.UploadedDocument,
                    CourseId = courseId,
                    LessonId = lessonId,
                    SourceFileName = fileName,
                    SourceFileUrl = sourceFileUrl,
                    ChunkIndex = chunkIndex++,
                    TextContent = batch[i],
                    VectorData = VectorHelper.ToBytes(vectorList[i]),
                    VectorDimension = vectorList[i].Length,
                    EmbeddingModel = _embedding.Model
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Da nap tai lieu {FileName}: {ChunkCount} doan, model {Model}.",
            fileName, textChunkList.Count, _embedding.Model);

        return new DocumentIngestResultDto(
            fileName, textChunkList.Count, extractedText.Length, _embedding.Model, removedCount);
    }

    public async Task<List<DocumentChunkMatchDto>> SearchAsync(
        string query, int topCount = 5, int? courseId = null,
        List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Vector câu hỏi: cùng một câu hỏi (cả phòng cùng hỏi, hoặc người dùng bấm hỏi lại) không phải đi Ollama
        // lần nữa. Khóa gồm tên model nên đổi model nhúng là tự bỏ bản cũ.
        var queryVector = await GetQueryVectorAsync(query, cancellationToken);
        if (queryVector.Length == 0) return [];

        // Database quan hệ không có kiểu vector nên cosine tính ở bộ nhớ. Kho vector được giữ sẵn trong
        // ChunkVectorIndex (làm tươi bằng vân tay) thay vì kéo cả bảng lên ở mỗi câu hỏi.
        var entryList = await _index.GetAsync(_db, _embedding.Model, cancellationToken);
        var queryNorm = ChunkVectorIndex.Norm(queryVector);

        // Hàng rào quyền đọc: tài liệu dùng chung toàn công ty (CourseId = null) thì ai cũng đọc được,
        // còn tài liệu gắn với một khóa học chỉ mở cho người được phân công khóa đó.
        var allowedSet = allowedCourseIdList is null ? null : new HashSet<int>(allowedCourseIdList);
        var queryPhraseList = GetWordPairList(query);

        return [.. entryList
            .Where(x => x.Vector.Length == queryVector.Length)
            .Where(x => courseId == null || x.CourseId == courseId || x.CourseId == null)
            .Where(x => allowedSet is null || x.CourseId == null || allowedSet.Contains(x.CourseId.Value))
            .Select(x => new DocumentChunkMatchDto(
                x.Id, x.SourceFileName, x.ChunkIndex, x.TextContent,
                ChunkVectorIndex.Cosine(queryVector, queryNorm, x)
                    + LexicalWeight * GetPhraseCoverage(queryPhraseList, x.NormalizedText),
                x.CourseId, x.LessonId))
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(topCount, 1, 50))];
    }

    private static readonly TimeSpan QueryVectorCacheDuration = TimeSpan.FromHours(1);

    private async Task<float[]> GetQueryVectorAsync(string query, CancellationToken cancellationToken)
    {
        var queryText = query.Trim();
        var queryKey = $"qemb|{_embedding.Model}|{queryText.ToLowerInvariant()}";
        if (_cache.TryGetValue<float[]>(queryKey, out var cached) && cached is not null) return cached;

        var vector = await _embedding.GetEmbeddingAsync(queryText, cancellationToken);
        if (vector.Length > 0) _cache.Set(queryKey, vector, QueryVectorCacheDuration);
        return vector;
    }

    /// <summary>Sinh sẵn vector câu hỏi vào bộ đệm để chạy SONG SONG với các lượt đọc database của người gọi
    /// (không đụng tới _db nên an toàn). Lỗi nuốt hết: tìm kiếm thật sẽ tự gọi lại và báo lỗi đúng chỗ.</summary>
    public async Task PrefetchQueryVectorAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try { await GetQueryVectorAsync(query, cancellationToken); }
        catch { /* xem summary */ }
    }

    /// <summary>Trọng số phần "khớp từ ngữ" cộng vào điểm vector.
    ///
    /// Model nhúng (nomic-embed-text) hiểu tiếng Việt kém: đo trên tài liệu PCCC, câu "Tứ diện cháy gồm những
    /// yếu tố nào?" chỉ ra toàn đoạn lạc đề, dù đúng cụm "tứ diện cháy" nằm sờ sờ trong tài liệu. Tiếng Việt
    /// viết theo âm tiết nên so theo CẶP âm tiết liền nhau ("tứ diện", "diện cháy") là đủ bắt được thuật ngữ
    /// mà không cần bộ tách từ. Với 0.3, một đoạn chứa trọn các cụm của câu hỏi vượt lên trên những đoạn
    /// chỉ "na ná" về vector, còn câu hỏi diễn đạt khác tài liệu thì vẫn do điểm vector quyết định.</summary>
    private const double LexicalWeight = 0.3;

    /// <summary>Các cặp âm tiết liền nhau của câu hỏi, viết thường.</summary>
    private static List<string> GetWordPairList(string text)
    {
        var wordList = SplitWordList(text);
        return [.. Enumerable.Range(0, Math.Max(0, wordList.Count - 1))
            .Select(i => $"{wordList[i]} {wordList[i + 1]}")
            .Distinct()];
    }

    /// <summary>Tỉ lệ cặp âm tiết của câu hỏi có mặt trong đoạn (0 → 1).</summary>
    private static double GetPhraseCoverage(List<string> phraseList, string normalizedContent)
    {
        if (phraseList.Count == 0) return 0;

        return phraseList.Count(x => normalizedContent.Contains($" {x} ", StringComparison.Ordinal)) / (double)phraseList.Count;
    }

    private static List<string> SplitWordList(string text) => ChunkVectorIndex.SplitWordList(text);

    /// <summary>Đoạn có điểm thấp hơn đoạn tốt nhất quá mức này thì gần như không liên quan tới câu hỏi —
    /// nhét vào câu lệnh chỉ làm model đọc lâu hơn và dễ trả lời lạc đề.</summary>
    private const double ScoreGapLimit = 0.12;

    /// <summary>Số đoạn tốt nhất được bù thêm ngữ cảnh liền kề.</summary>
    private const int NeighborExpandCount = 3;

    public async Task<List<DocumentChunkMatchDto>> GetAnswerContextAsync(
        string question, int topCount = 5, int? courseId = null,
        List<int>? allowedCourseIdList = null, CancellationToken cancellationToken = default)
    {
        var matchList = await SearchAsync(question, topCount, courseId, allowedCourseIdList, cancellationToken);
        if (matchList.Count == 0) return [];

        var bestScore = matchList[0].Score;
        matchList = [.. matchList.Where(x => bestScore - x.Score <= ScoreGapLimit)];

        // Đoạn khớp nhất thường bị cắt ngang giữa ý; lấy kèm đoạn liền trước và liền sau để model đọc trọn ý.
        var neighborKeyList = matchList
            .Take(NeighborExpandCount)
            .SelectMany(x => new[] { (x.SourceFileName, Index: x.ChunkIndex - 1), (x.SourceFileName, Index: x.ChunkIndex + 1) })
            .Where(x => x.Index >= 0)
            .ToList();

        var chosenIdList = matchList.Select(x => x.Id).ToHashSet();
        var fileNameList = neighborKeyList.Select(x => x.SourceFileName).Distinct().ToList();
        var indexList = neighborKeyList.Select(x => x.Index).Distinct().ToList();

        // Truy vấn thô theo (tên file, số thứ tự) rồi lọc lại đúng cặp ở bộ nhớ — LINQ to SQL không dịch được
        // phép so khớp theo cặp, mà số dòng lấy về ở đây rất nhỏ nên lọc lại không tốn kém.
        var neighborQuery = _db.DocumentChunk.AsNoTracking()
            .Where(x => fileNameList.Contains(x.SourceFileName)
                        && indexList.Contains(x.ChunkIndex)
                        && !chosenIdList.Contains(x.Id)
                        && (courseId == null || x.CourseId == courseId || x.CourseId == null));

        // Đoạn liền kề cũng phải qua đúng hàng rào quyền đọc như đoạn khớp câu hỏi: rào ở SearchAsync mà
        // quên ở đây thì tài liệu của khóa học người hỏi không được phân công vẫn lọt vào câu lệnh gửi cho
        // model, chỉ vì nó nằm ngay cạnh một đoạn hợp lệ trong cùng một file.
        if (allowedCourseIdList is not null)
            neighborQuery = neighborQuery.Where(
                x => x.CourseId == null || allowedCourseIdList.Contains(x.CourseId.Value));

        var neighborList = await neighborQuery
            .Select(x => new { x.Id, x.SourceFileName, x.ChunkIndex, x.TextContent, x.CourseId, x.LessonId })
            .ToListAsync(cancellationToken);

        var neighborKeySet = neighborKeyList.ToHashSet();
        var contextList = matchList.ToList();
        contextList.AddRange(neighborList
            .Where(x => neighborKeySet.Contains((x.SourceFileName, x.ChunkIndex)))
            .Select(x => new DocumentChunkMatchDto(x.Id, x.SourceFileName, x.ChunkIndex, x.TextContent, 0, x.CourseId, x.LessonId)));

        // Sắp theo: tài liệu khớp nhất trước, trong mỗi tài liệu thì đúng thứ tự đoạn như bản gốc —
        // đọc lộn xộn thì model dễ ghép nhầm thông tin của hai đoạn rời rạc thành một câu sai.
        var fileRank = matchList
            .GroupBy(x => x.SourceFileName)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Score));

        return [.. contextList
            .OrderByDescending(x => fileRank.TryGetValue(x.SourceFileName, out var rank) ? rank : 0)
            .ThenBy(x => x.SourceFileName)
            .ThenBy(x => x.ChunkIndex)];
    }

    public async Task<int> DeleteBySourceAsync(string sourceFileName, int? courseId = null, CancellationToken cancellationToken = default)
        => await _db.DocumentChunk
            // Chỉ đụng tới TÀI LIỆU TẢI LÊN. Nội dung khóa học do máy đồng bộ cũng nằm chung bảng này và
            // được nhận dạng bằng (loại nguồn + khóa học + bài học) chứ không bằng tên — xóa nó ở đây thì
            // vừa xóa nhầm, vừa vô ích vì lượt đồng bộ kế tiếp nạp lại ngay (xem CourseKnowledgeService).
            .Where(x => x.SourceKind == KnowledgeSourceKind.UploadedDocument)
            .Where(x => x.SourceFileName == sourceFileName && (courseId == null || x.CourseId == courseId))
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Gộp khoảng trắng thừa (PDF hay xuống dòng giữa câu) nhưng giữ lại ranh giới đoạn văn.</summary>
    private static string NormalizeWhitespace(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{2,}", "\n\n");   // nhiều dòng trống -> một ranh giới đoạn
        return text.Trim();
    }
}
