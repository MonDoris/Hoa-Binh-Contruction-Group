// File: Application/Services/CourseKnowledgeService.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

/// <summary>Biến nội dung khóa học (mô tả khóa + từng bài học, KỂ CẢ file PDF/Word/TXT đính kèm bài học) thành các đoạn có vector nhúng trong kho tri
/// thức dùng chung với tài liệu tải lên.
///
/// Vì sao nạp vào CHUNG một bảng thay vì tra thẳng bảng bài học lúc trả lời: câu hỏi của người học là ngôn
/// ngữ tự nhiên ("làm sao xin nghỉ phép nửa ngày"), không phải từ khóa. Chỉ tìm ngữ nghĩa trên vector mới ra
/// đúng đoạn, mà đã dùng vector thì dùng luôn đường tìm kiếm + hàng rào quyền đọc đã có sẵn ở
/// <see cref="DocumentProcessingService"/>. Thêm một đường tìm kiếm thứ hai riêng cho bài học nghĩa là nhân
/// đôi cả phần chọn đoạn lẫn phần rào quyền — và hai bản rào quyền thì sớm muộn sẽ lệch nhau.
///
/// HÀNG RÀO QUYỀN: mỗi đoạn sinh ra ở đây LUÔN mang CourseId của khóa học nó thuộc về, nên nó tự nằm trong
/// đúng phạm vi mà AiTutorService đã dựng sẵn — người học chỉ đọc được khóa mình được phân công. Đây là
/// lý do KHÔNG nạp nội dung bài kiểm tra: câu hỏi kèm đáp án mà lọt vào câu trả lời thì học viên hỏi HB Minerva
/// là biết trước đáp án bài thi.</summary>
public class CourseKnowledgeService : ICourseKnowledgeService
{
    /// <summary>Số ký tự tối đa của một đoạn — giữ đúng bằng bản nạp tài liệu để hai nguồn cho ra những đoạn
    /// cùng cỡ. Lệch cỡ thì đoạn dài luôn thắng ở phần "bù ngữ cảnh" và chiếm hết ngân sách câu lệnh.</summary>

    /// <summary>Số đoạn gửi Ollama mỗi lần gọi.</summary>
    private const int EmbeddingBatchSize = 16;

    /// <summary>Bài học ngắn hơn ngần này ký tự thì bỏ qua: một bài chỉ có mỗi dòng "Xem video đính kèm"
    /// không mang thông tin gì, nạp vào chỉ làm loãng kết quả tìm kiếm.</summary>
    private const int MinTextLength = 60;

    /// <summary>Định dạng file đính kèm bài học mà HB Minerva đọc được — khớp với <see cref="IDocumentTextExtractor"/>.
    /// Video/hình ảnh không có chữ để học nên bỏ qua.</summary>
    private static readonly string[] ReadableFileExtensionList = [".pdf", ".docx", ".txt", ".md"];

    /// <summary>Chỉ cho MỘT lượt học chạy tại một thời điểm trong cả tiến trình. Hàng đợi "học ngay", lượt quét
    /// định kỳ và nút "Học ngay" của quản trị viên có thể trùng nhau; chạy song song thì hai lượt cùng
    /// xóa-rồi-nạp một bài học và kho tri thức bị nhân đôi đoạn.</summary>
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    private readonly IAppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly IAppPaths _paths;
    private readonly ILogger<CourseKnowledgeService> _logger;

    public CourseKnowledgeService(IAppDbContext db, IEmbeddingService embedding, IDocumentTextExtractor textExtractor,
        IAppPaths paths, ILogger<CourseKnowledgeService> logger)
    {
        _db = db;
        _embedding = embedding;
        _textExtractor = textExtractor;
        _paths = paths;
        _logger = logger;
    }

    public async Task<CourseKnowledgeSyncResultDto> SyncAsync(
        bool isForced = false, int? courseId = null, CancellationToken cancellationToken = default)
    {
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            return await SyncCoreAsync(isForced, courseId, cancellationToken);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<CourseKnowledgeSyncResultDto> SyncCoreAsync(
        bool isForced, int? courseId, CancellationToken cancellationToken)
    {
        var status = await _embedding.CheckStatusAsync(cancellationToken: cancellationToken);
        if (!status.IsReady)
            throw new InvalidOperationException(status.Message);

        var courseList = await GetLearnableCourseListAsync(courseId, cancellationToken);

        // Các đoạn nội dung khóa học ĐANG có trong kho, gom theo đơn vị nạp (khóa học / bài học). Lấy một
        // lượt rồi đối chiếu trong bộ nhớ, thay vì hỏi database một câu cho mỗi bài học: một công ty vài chục
        // khóa học là vài trăm bài, tức vài trăm lượt đi lại chỉ để phát hiện "không có gì đổi".
        var existingList = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceKind != KnowledgeSourceKind.UploadedDocument)
            .Select(x => new { x.SourceKind, x.CourseId, x.LessonId, x.SourceHash })
            .ToListAsync(cancellationToken);

        var existingHashMap = existingList
            .GroupBy(x => (x.SourceKind, x.CourseId, x.LessonId))
            .ToDictionary(g => g.Key, g => g.First().SourceHash);

        var indexedCourseCount = 0;
        var indexedUnitCount = 0;
        var skippedUnitCount = 0;
        var chunkCount = 0;

        // Đơn vị nạp còn "sống" sau lượt này — mọi thứ ngoài danh sách đó là rác của bài học/khóa học đã xóa
        // hoặc đã thôi không còn ai học, dọn ở cuối hàm.
        var aliveKeySet = new HashSet<(KnowledgeSourceKind, int?, int?)>();

        foreach (var course in courseList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var unitList = BuildUnitList(course);
            var hasChange = false;

            foreach (var unit in unitList)
            {
                // Vân tay trùng nghĩa là chữ trong bài học y hệt lần nạp trước. Sinh lại vector cho nó chỉ
                // tốn hàng chục giây của Ollama để ra đúng những con số cũ.
                if (!isForced
                    && existingHashMap.TryGetValue(unit.Key, out var oldHash)
                    && oldHash == unit.Hash)
                {
                    aliveKeySet.Add(unit.Key);
                    skippedUnitCount++;
                    continue;
                }

                string text;
                try
                {
                    text = await unit.LoadTextAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // File đính kèm hỏng / bị xóa khỏi ổ đĩa / định dạng lạ. Chỉ bỏ qua đúng bài này — một file
                    // lỗi không được chặn cả công ty học các khóa khác. Giữ nguyên phần đã học từ lần trước
                    // (đánh dấu còn sống) vì lỗi đọc file thường là tạm thời.
                    _logger.LogWarning(ex, "Khong doc duoc noi dung {Label} de nap kien thuc.", unit.Label);
                    aliveKeySet.Add(unit.Key);
                    continue;
                }

                // Bài quá ngắn (PDF dạng ảnh scan không có chữ chẳng hạn) thì không nạp, và phần cũ nếu có sẽ
                // bị dọn vì không nằm trong aliveKeySet.
                if (text.Length < MinTextLength) continue;

                aliveKeySet.Add(unit.Key);
                chunkCount += await SaveUnitAsync(course, unit, text, cancellationToken);
                indexedUnitCount++;
                hasChange = true;
            }

            if (hasChange) indexedCourseCount++;
        }

        var removedChunkCount = await RemoveOrphanChunkAsync(aliveKeySet, courseId, cancellationToken);

        _logger.LogInformation(
            "Dong bo kien thuc khoa hoc: {CourseCount} khoa co thay doi, {UnitCount} muc nap moi ({ChunkCount} doan), " +
            "{SkippedCount} muc khong doi, {RemovedCount} doan da don.",
            indexedCourseCount, indexedUnitCount, chunkCount, skippedUnitCount, removedChunkCount);

        return new CourseKnowledgeSyncResultDto(
            courseList.Count, indexedCourseCount, indexedUnitCount, skippedUnitCount, chunkCount, removedChunkCount);
    }

    public async Task<CourseKnowledgeStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // Đếm bài học bằng SQL chứ KHÔNG dùng lại GetLearnableCourseListAsync: hàm kia kéo trọn nội dung
        // HTML của mọi bài học lên bộ nhớ, đúng thứ cần cho việc nạp nhưng vô nghĩa cho một bảng đếm số —
        // vài chục khóa học là vài chục megabyte chữ đi qua mạng chỉ để trả về mấy con số.
        //
        // Mẫu số là "bài học đang hoạt động và có nội dung". Nó xấp xỉ chứ không trùng khít điều kiện nạp
        // thật (bài quá ngắn sau khi bóc thẻ HTML thì bị bỏ qua), nên một bài chỉ có vài chữ sẽ mãi hiện là
        // "học một phần" — đã nói rõ trong phần hướng dẫn ở màn hình quản trị.
        var courseInfoList = await _db.Course.AsNoTracking()
            .Where(x => x.Status == CourseStatus.Published || x.EnrollmentList.Any())
            .Select(x => new
            {
                x.Id,
                x.Title,
                LessonCount = x.LessonList.Count(lesson =>
                    lesson.IsActive
                    && ((lesson.Content != null && lesson.Content.Length >= MinTextLength)
                        || (lesson.ResourceUrl != null
                            && (lesson.ResourceUrl.EndsWith(".pdf") || lesson.ResourceUrl.EndsWith(".docx")
                                || lesson.ResourceUrl.EndsWith(".txt") || lesson.ResourceUrl.EndsWith(".md")))))
            })
            .ToListAsync(cancellationToken);

        var chunkGroupList = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceKind != KnowledgeSourceKind.UploadedDocument && x.CourseId != null)
            .GroupBy(x => x.CourseId!.Value)
            .Select(g => new
            {
                CourseId = g.Key,
                ChunkCount = g.Count(),
                LessonCount = g.Where(x => x.LessonId != null).Select(x => x.LessonId).Distinct().Count(),
                SyncedAt = g.Max(x => x.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        var chunkGroupMap = chunkGroupList.ToDictionary(x => x.CourseId);

        var itemList = courseInfoList
            .Select(course =>
            {
                chunkGroupMap.TryGetValue(course.Id, out var group);
                return new CourseKnowledgeItemDto(
                    course.Id,
                    course.Title,
                    course.LessonCount,
                    group?.LessonCount ?? 0,
                    group?.ChunkCount ?? 0,
                    group?.SyncedAt);
            })
            .OrderBy(x => x.ChunkCount == 0 ? 0 : 1)   // khóa chưa học xong đứng trước — đó là thứ cần nhìn
            .ThenBy(x => x.CourseTitle)
            .ToList();

        var embeddingStatus = await _embedding.CheckStatusAsync(cancellationToken: cancellationToken);

        return new CourseKnowledgeStatusDto(
            itemList.Count,
            itemList.Count(x => x.ChunkCount > 0),
            itemList.Sum(x => x.ChunkCount),
            itemList.Max(x => x.SyncedAt),
            embeddingStatus.IsReady,
            embeddingStatus.IsReady
                ? "Máy chủ AI sẵn sàng học nội dung khóa học."
                : embeddingStatus.Message,
            itemList);
    }

    // ==================== CHỌN KHÓA HỌC ====================

    /// <summary>Những khóa học mà người dùng ĐANG ĐƯỢC HỌC — đúng phạm vi yêu cầu "AI học các khóa học mà
    /// người dùng được học".
    ///
    /// Gồm hai nhóm: khóa đã xuất bản (ai được phân công cũng học được) và khóa đã có người được phân công
    /// dù trạng thái hiện tại là gì. Nhóm thứ hai không thừa: một khóa bị chuyển sang Lưu trữ vẫn còn người
    /// đang học dở, và câu hỏi của họ vẫn phải trả lời được.
    ///
    /// Bản nháp/đang chờ duyệt thì KHÔNG nạp: nội dung chưa ai duyệt mà AI đã đem đi trả lời cho cả công ty
    /// thì cả chuỗi duyệt nội dung nhiều cấp của hệ thống thành vô nghĩa.</summary>
    private async Task<List<Course>> GetLearnableCourseListAsync(int? courseId, CancellationToken cancellationToken)
        => await _db.Course.AsNoTracking()
            .Include(x => x.LessonList.Where(lesson => lesson.IsActive))
            .Where(x => courseId == null || x.Id == courseId)
            .Where(x => x.Status == CourseStatus.Published || x.EnrollmentList.Any())
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    // ==================== DỰNG NỘI DUNG ĐỂ NẠP ====================

    /// <summary>Một "đơn vị nạp": phần giới thiệu khóa học, hoặc một bài học. Chia theo đơn vị này chứ không
    /// gộp cả khóa thành một khối, vì sửa một bài thì chỉ phải nạp lại đúng bài đó.
    ///
    /// Phần chữ được đọc LƯỜI qua LoadTextAsync: bóc chữ một file PDF dài tốn vài giây, nên chỉ làm khi vân
    /// tay cho thấy file đã đổi.</summary>
    private sealed record KnowledgeUnit(
        (KnowledgeSourceKind Kind, int? CourseId, int? LessonId) Key,
        string Label,
        string Hash,
        Func<CancellationToken, Task<string>> LoadTextAsync);

    private List<KnowledgeUnit> BuildUnitList(Course course)
    {
        var unitList = new List<KnowledgeUnit>();

        var overviewText = BuildOverviewText(course);
        if (overviewText.Length >= MinTextLength)
            unitList.Add(new KnowledgeUnit(
                (KnowledgeSourceKind.CourseOverview, course.Id, null),
                BuildLabel(course.Title, "Giới thiệu khóa học"),
                ComputeHash(overviewText),
                _ => Task.FromResult(overviewText)));

        foreach (var lesson in course.LessonList.OrderBy(x => x.OrderIndex).ThenBy(x => x.Id))
        {
            var key = (KnowledgeSourceKind.CourseLesson, (int?)course.Id, (int?)lesson.Id);
            var label = BuildLabel(course.Title, lesson.Title);
            var lessonText = BuildLessonText(lesson);
            var file = ResolveLessonFile(lesson);

            if (file is null)
            {
                if (lessonText.Length < MinTextLength) continue;

                unitList.Add(new KnowledgeUnit(key, label, ComputeHash(lessonText), _ => Task.FromResult(lessonText)));
                continue;
            }

            // Bài học dạng TÀI LIỆU: nội dung thật nằm trong file (khóa PCCC chẳng hạn là một PDF 18 trang,
            // còn phần chữ của bài học thì trống). Vân tay gồm chữ của bài học + dấu hiệu nhận biết file
            // (đường dẫn, dung lượng, giờ sửa) — thay file là đổi vân tay, file y nguyên thì khỏi bóc chữ lại.
            var fileHash = ComputeHash(
                $"{lessonText}|file:{lesson.ResourceUrl}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");

            unitList.Add(new KnowledgeUnit(key, label, fileHash, async cancellationToken =>
            {
                var extension = file.Extension.TrimStart('.').ToLowerInvariant();
                // PdfPig đọc đồng bộ và ngốn CPU — đẩy khỏi luồng đang chờ.
                var fileText = await Task.Run(
                    () => _textExtractor.ExtractTextAsync(file.FullName, extension, cancellationToken), cancellationToken);

                var body = NormalizeWhitespace(fileText);
                if (body.Length == 0) return string.Empty;

                return lessonText.Length > 0
                    ? $"{lessonText}\n\n{body}"
                    : NormalizeWhitespace($"Bài học: {lesson.Title}.\n{body}");
            }));
        }

        return unitList;
    }

    /// <summary>File tài liệu đính kèm bài học trên ổ đĩa, hoặc null nếu bài không có file đọc được chữ.
    ///
    /// Chỉ nhận đường dẫn NẰM TRONG thư mục web: ResourceUrl là dữ liệu người soạn gửi lên, không kiểm tra thì
    /// một chuỗi kiểu "../../appsettings.json" là đủ để đem cấu hình máy chủ vào câu trả lời của HB Minerva.</summary>
    private FileInfo? ResolveLessonFile(Lesson lesson)
    {
        var url = lesson.ResourceUrl?.Trim();
        if (string.IsNullOrEmpty(url) || url.Contains("://") || url.StartsWith("//")) return null;

        var queryIndex = url.IndexOfAny(['?', '#']);
        if (queryIndex >= 0) url = url[..queryIndex];

        if (!ReadableFileExtensionList.Contains(Path.GetExtension(url).ToLowerInvariant())) return null;

        var webRoot = Path.GetFullPath(string.IsNullOrEmpty(_paths.WebRootPath)
            ? Path.Combine(_paths.ContentRootPath, "wwwroot")
            : _paths.WebRootPath);

        var fullPath = Path.GetFullPath(Path.Combine(
            webRoot, Uri.UnescapeDataString(url).TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var file = new FileInfo(fullPath);
        if (file.Exists) return file;

        _logger.LogWarning("Bai hoc {LessonId} tro toi file khong ton tai: {Url}", lesson.Id, lesson.ResourceUrl);
        return null;
    }

    /// <summary>Phần giới thiệu khóa học: tên, mô tả và MỤC LỤC các bài. Có mục lục thì những câu hỏi kiểu
    /// "khóa an toàn lao động gồm những bài nào" mới trả lời được — không bài học riêng lẻ nào chứa câu trả
    /// lời đó, vì mỗi bài chỉ biết về chính nó.</summary>
    private static string BuildOverviewText(Course course)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Khóa học: {course.Title} (mã {course.Code}).");

        var description = HtmlToText(course.Description);
        if (description.Length > 0) builder.AppendLine($"Giới thiệu: {description}");

        if (course.DurationMinutes > 0)
            builder.AppendLine($"Thời lượng dự kiến: {course.DurationMinutes} phút.");
        builder.AppendLine(course.IsMandatory
            ? "Đây là khóa học bắt buộc với đối tượng được phân công."
            : "Đây là khóa học không bắt buộc.");

        var lessonTitleList = course.LessonList
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
            .Select((x, i) => $"{i + 1}. {x.Title}")
            .ToList();

        if (lessonTitleList.Count > 0)
        {
            builder.AppendLine($"Khóa học gồm {lessonTitleList.Count} bài học:");
            foreach (var title in lessonTitleList) builder.AppendLine(title);
        }

        return NormalizeWhitespace(builder.ToString());
    }

    /// <summary>Nội dung một bài học đã bóc khỏi HTML. Luôn mở đầu bằng TÊN BÀI: đoạn cắt ra ở giữa bài
    /// thường không nhắc lại chủ đề, mà vector của một đoạn không có chủ đề thì gần như không khớp được câu
    /// hỏi nào của người học — họ luôn hỏi bằng tên chủ đề.</summary>
    private static string BuildLessonText(Lesson lesson)
    {
        var content = HtmlToText(lesson.Content);
        if (content.Length == 0) return string.Empty;

        return NormalizeWhitespace($"Bài học: {lesson.Title}.\n{content}");
    }

    /// <summary>Nhãn hiện ở bảng "Nguồn tham khảo" dưới câu trả lời. Ghi kèm tên khóa học vì hai khóa rất hay
    /// có bài trùng tên ("Bài 1: Tổng quan"), mà người đọc cần biết câu trả lời lấy từ khóa nào.
    /// Cắt vừa 255 ký tự — đúng trần của cột SourceFileName.</summary>
    private static string BuildLabel(string courseTitle, string unitTitle)
    {
        var label = $"{courseTitle} › {unitTitle}";
        return label.Length <= 255 ? label : label[..255];
    }

    // ==================== NẠP MỘT ĐƠN VỊ ====================

    /// <summary>Xóa đoạn cũ của đúng đơn vị này rồi nạp lại. Trả về số đoạn đã nạp.</summary>
    private async Task<int> SaveUnitAsync(Course course, KnowledgeUnit unit, string text, CancellationToken cancellationToken)
    {
        var (kind, unitCourseId, lessonId) = unit.Key;

        // Xóa theo KHÓA ĐƠN VỊ (loại nguồn + khóa học + bài học), không theo tên: bài học đổi tên là nhãn
        // đổi theo, xóa theo nhãn thì bản cũ ở lại vĩnh viễn và AI trả lời bằng nội dung đã bị thay.
        await _db.DocumentChunk
            .Where(x => x.SourceKind == kind && x.CourseId == unitCourseId && x.LessonId == lessonId)
            .ExecuteDeleteAsync(cancellationToken);

        var textChunkList = TextChunker.SplitIntoChunks(text);

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
                    SourceKind = kind,
                    CourseId = unitCourseId,
                    LessonId = lessonId,
                    SourceFileName = unit.Label,
                    SourceHash = unit.Hash,
                    ChunkIndex = chunkIndex++,
                    TextContent = batch[i],
                    VectorData = VectorHelper.ToBytes(vectorList[i]),
                    VectorDimension = vectorList[i].Length,
                    EmbeddingModel = _embedding.Model
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Bản ghi vừa thêm còn nằm trong ChangeTracker; giữ lại thì lượt nạp sau của cùng một lần đồng bộ
        // kéo theo cả nghìn thực thể cũ và mỗi lần SaveChanges lại chậm thêm.
        _db.ChangeTracker.Clear();

        _logger.LogDebug("Da nap kien thuc {Label} cua khoa {CourseId}: {ChunkCount} doan.",
            unit.Label, course.Id, textChunkList.Count);

        return textChunkList.Count;
    }

    /// <summary>Dọn đoạn của bài học/khóa học đã xóa, đã bị ẩn, hoặc đã rời khỏi phạm vi "được học".
    ///
    /// Không dọn thì AI vẫn trả lời bằng nội dung của một bài học không còn tồn tại — hỏng hơn hẳn việc
    /// thiếu thông tin, vì người hỏi không có cách nào biết là mình vừa đọc phải bản đã gỡ.</summary>
    private async Task<int> RemoveOrphanChunkAsync(
        HashSet<(KnowledgeSourceKind, int?, int?)> aliveKeySet, int? courseId, CancellationToken cancellationToken)
    {
        // Đồng bộ MỘT khóa học thì chỉ được dọn trong phạm vi khóa đó: mọi khóa khác không nằm trong lượt
        // chạy này nên không có mặt trong aliveKeySet, coi chúng là rác là xóa sạch cả kho.
        var candidateList = await _db.DocumentChunk.AsNoTracking()
            .Where(x => x.SourceKind != KnowledgeSourceKind.UploadedDocument)
            .Where(x => courseId == null || x.CourseId == courseId)
            .Select(x => new { x.Id, x.SourceKind, x.CourseId, x.LessonId })
            .ToListAsync(cancellationToken);

        var orphanIdList = candidateList
            .Where(x => !aliveKeySet.Contains((x.SourceKind, x.CourseId, x.LessonId)))
            .Select(x => x.Id)
            .ToList();

        if (orphanIdList.Count == 0) return 0;

        return await _db.DocumentChunk
            .Where(x => orphanIdList.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // ==================== TIỆN ÍCH ====================

    /// <summary>Vân tay của một đơn vị nạp. Có kèm TÊN MODEL NHÚNG: đổi model là vector cũ hết so sánh được
    /// với vector mới, nên phải coi như nội dung đã đổi và nạp lại — nếu chỉ băm phần chữ thì lượt đồng bộ
    /// sau khi đổi model sẽ bỏ qua sạch và kho tri thức kẹt lại ở model cũ.</summary>
    private string ComputeHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{_embedding.Model}|{text}")));

    /// <summary>Bóc chữ khỏi HTML của trình soạn thảo: bỏ hẳn script/style, đổi thẻ khối thành ranh giới
    /// dòng (không có bước này thì "&lt;li&gt;A&lt;/li&gt;&lt;li&gt;B&lt;/li&gt;" dính thành "AB"), rồi giải mã thực thể HTML.</summary>
    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        text = Regex.Replace(text, "<(br|/p|/div|/li|/h[1-6]|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</t[dh]>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");

        return NormalizeWhitespace(System.Net.WebUtility.HtmlDecode(text));
    }

    /// <summary>Gộp khoảng trắng thừa nhưng giữ ranh giới đoạn — cùng cách xử lý với bản nạp tài liệu, để
    /// hai nguồn cho ra những đoạn cùng dạng.</summary>
    private static string NormalizeWhitespace(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, "&nbsp;", " ");
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" ?\n ?", "\n");
        text = Regex.Replace(text, @"\n{2,}", "\n\n");
        return text.Trim();
    }
}
