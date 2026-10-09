// File: Application/Features/Scorm/Export/ScormCourseExporter.cs
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HbELearning.Application.Features.Scorm.CoursePackage;
using HbELearning.Application.Features.Scorm.Manifest;
using HbELearning.Application.Features.Scorm.Model;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Scorm.Export;

/// <summary>
/// Xuất khóa học thành gói SCORM 2004 4th Edition (ĐẦU RA của khóa học) để mang sang LMS khác.
///
/// Cây hoạt động của gói xuất là CHÍNH cây trình phát của hệ thống đang chạy (CourseOrganizationBuilder, chế độ
/// xuất) — cùng chương, cùng thứ tự, cùng luật tuần tự / lịch / giới hạn lượt / trọng số điểm — ghi ra
/// imsmanifest.xml bằng <see cref="ScormManifestWriter"/>. Chỉ phần TÀI NGUYÊN là khác: SCO của hệ thống (trang
/// /pages/sco-*.html cần máy chủ này) được thay bằng trang HTML tĩnh tự chứa:
/// - Bài học → trang tĩnh, tệp đính kèm / ảnh trong nội dung đóng vào gói; báo hoàn thành khi học viên bấm.
/// - Bài kiểm tra → trang tự chấm, báo điểm (score.*), đạt / không đạt và từng câu trả lời (cmi.interactions).
/// - Gói SCORM 2004 đã nhập → nhúng nguyên cây + tệp của gói gốc.
/// - Mô phỏng 3D và gói SCORM 1.2 → bỏ qua và nói rõ trong bản xem trước.
/// </summary>
public class ScormCourseExporter
{
    private const string SharedResourceId = "RES-SHARED";

    private readonly IAppDbContext _db;
    private readonly IScormPackageStorage _storage;
    private readonly CourseOrganizationBuilder _builder;

    public ScormCourseExporter(IAppDbContext db, IScormPackageStorage storage, CourseOrganizationBuilder builder)
    {
        _db = db;
        _storage = storage;
        _builder = builder;
    }

    private static string KindLabel(CourseResourceKind kind) => kind switch
    {
        CourseResourceKind.Lesson => "Lesson",
        CourseResourceKind.Quiz => "Quiz",
        CourseResourceKind.Simulation => "Simulation",
        _ => "Scorm",
    };

    // ==================== XEM TRƯỚC ====================

    public async Task<ScormExportPreviewDto?> BuildPreviewAsync(int courseId)
    {
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return null;

        var organization = await _builder.BuildAsync(courseId, forExport: true);
        // Bài học có tệp đính kèm nội bộ đã mất trên đĩa — báo trước để người xuất còn kịp bổ sung lại.
        var lessonUrlById = await _db.Lesson.AsNoTracking().Where(x => x.CourseId == courseId && x.ResourceUrl != null)
            .ToDictionaryAsync(x => x.Id, x => x.ResourceUrl!);

        var itemList = new List<ScormExportItemDto>();
        void Walk(ScormActivityDefinition activity)
        {
            foreach (var child in activity.Children)
            {
                if (organization.ResourceByActivityId.TryGetValue(child.Id, out var resource))
                {
                    var note = resource.Kind == CourseResourceKind.Lesson && lessonUrlById.TryGetValue(resource.Id, out var url)
                               && IsInternalUpload(url) && !UploadExists(url)
                        ? "Tệp đính kèm của bài này không còn trên máy chủ — gói vẫn có nội dung chữ nhưng thiếu tệp."
                        : null;
                    itemList.Add(new ScormExportItemDto(KindLabel(resource.Kind), child.Title, true, note));
                    continue; // gói nhúng: không liệt kê từng SCO bên trong
                }
                itemList.Add(new ScormExportItemDto("Module", child.Title, true, null));
                Walk(child);
            }
        }
        Walk(organization.Tree.Root);
        itemList.AddRange(organization.SkippedList.Select(x => new ScormExportItemDto(KindLabel(x.Resource.Kind), x.Resource.Title, false, x.Reason)));

        return new ScormExportPreviewDto(course.Id, course.Title, await OriginalPackageAsync(courseId) is not null, itemList);
    }

    /// <summary>Khóa học chỉ gồm đúng một gói SCORM nhập ngoài: trả nguyên gói gốc (giữ nguyên phiên bản, kể cả 1.2)
    /// thay vì bọc lại — bọc lại chỉ thêm một tầng cây vô nghĩa.</summary>
    private async Task<ScormPackage?> OriginalPackageAsync(int courseId)
    {
        var organization = await _builder.BuildAsync(courseId);
        var resourceList = organization.ResourceByActivityId.Values.ToList();
        if (resourceList.Count != 1 || resourceList[0].Kind != CourseResourceKind.Package) return null;
        return await _db.ScormPackage.AsNoTracking().FirstOrDefaultAsync(x => x.Id == resourceList[0].Id);
    }

    // ==================== MANIFEST / XUẤT ====================

    /// <summary>imsmanifest.xml đúng như trong gói xuất — dựng "khô" (không đóng tệp nào), để người soạn xem.</summary>
    public async Task<string?> BuildManifestAsync(int courseId)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == courseId)) return null;
        var organization = await _builder.BuildAsync(courseId, forExport: true);
        var document = await WritePackageAsync(organization, new ExportContext(null, courseId));
        return ScormManifestWriter.ToXmlString(document);
    }

    public async Task<(Stream? Stream, string FileName, string? ErrorMessage)?> ExportAsync(int courseId)
    {
        var course = await _db.Course.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return null;
        var safeCode = Regex.Replace(course.Code, "[^A-Za-z0-9_-]", "-");

        if (await OriginalPackageAsync(courseId) is ScormPackage original)
            return (await ZipOriginalPackageAsync(original), $"{safeCode}-scorm.zip", null);

        var organization = await _builder.BuildAsync(courseId, forExport: true);
        if (!organization.Tree.Root.Children.Any())
            return (null, "", "Khóa học chưa có nội dung nào đóng gói SCORM được (bài học, bài kiểm tra hoặc gói SCORM 2004).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"hbe-export-{Guid.NewGuid():N}.zip");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var context = new ExportContext(archive, courseId);
                await AddTextAsync(context, "shared/hbe-scorm.js", ScormExportTemplates.RuntimeScript);
                await AddTextAsync(context, "shared/hbe-style.css", ScormExportTemplates.StyleSheet);
                var document = await WritePackageAsync(organization, context);
                await AddTextAsync(context, "imsmanifest.xml", ScormManifestWriter.ToXmlString(document));
            }

            output.Position = 0;
            return (output, $"{safeCode}-scorm2004.zip", null);
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }

    public async Task<Stream> ZipOriginalPackageAsync(ScormPackage package)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"hbe-scorm-{Guid.NewGuid():N}.zip");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            await _storage.AddPackageToZipAsync(package.StorageKey, archive, "");
        output.Position = 0;
        return output;
    }

    /// <summary>Đóng tài nguyên của từng hoạt động lá vào gói (trừ khi chạy khô) rồi ghi manifest từ cây.</summary>
    private async Task<XDocument> WritePackageAsync(CourseOrganization organization, ExportContext context)
    {
        var specById = new Dictionary<string, ScormResourceSpec>(StringComparer.Ordinal);

        foreach (var activity in organization.Tree.Root.Children.SelectMany(Flatten))
        {
            if (!organization.ResourceByActivityId.TryGetValue(activity.Id, out var resource)) continue;
            switch (resource.Kind)
            {
                case CourseResourceKind.Lesson:
                    specById[activity.Id] = await ExportLessonAsync(context, resource.Id);
                    break;
                case CourseResourceKind.Quiz:
                    specById[activity.Id] = await ExportQuizAsync(context, resource.Id);
                    break;
                case CourseResourceKind.Package:
                    await EmbedPackageAsync(context, resource.Id, activity, specById);
                    break;
            }
        }

        var shared = new XElement(ScormManifestWriter.Cp + "resource",
            new XAttribute("identifier", SharedResourceId), new XAttribute("type", "webcontent"),
            new XAttribute(ScormManifestWriter.Adlcp + "scormType", "asset"),
            new XElement(ScormManifestWriter.Cp + "file", new XAttribute("href", "shared/hbe-scorm.js")),
            new XElement(ScormManifestWriter.Cp + "file", new XAttribute("href", "shared/hbe-style.css")));

        return ScormManifestWriter.Write(organization.Tree, a => specById.GetValueOrDefault(a.Id), new[] { shared });

        static IEnumerable<ScormActivityDefinition> Flatten(ScormActivityDefinition activity)
        {
            yield return activity;
            foreach (var child in activity.Children.SelectMany(Flatten)) yield return child;
        }
    }

    // ==================== BÀI HỌC ====================

    /// <summary>Archive null = chạy khô: chỉ tính tên tệp sẽ đóng, không ghi gì.</summary>
    private sealed class ExportContext
    {
        public ExportContext(ZipArchive? archive, int courseId) { Archive = archive; CourseId = courseId; }
        public ZipArchive? Archive { get; }
        public int CourseId { get; }
        /// <summary>url /uploads/... -> đường dẫn đã đóng vào gói (mỗi tệp chỉ đóng một lần dù nhiều bài dùng chung).</summary>
        public Dictionary<string, string?> AssetByUrl { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ScormResourceSpec> ExportLessonAsync(ExportContext context, int lessonId)
    {
        var lesson = await _db.Lesson.AsNoTracking().FirstAsync(x => x.Id == lessonId);
        var fileList = new List<string>();

        var media = "";
        var local = string.IsNullOrWhiteSpace(lesson.ResourceUrl) ? null : await PackAssetAsync(context, lesson.ResourceUrl);
        if (!string.IsNullOrWhiteSpace(lesson.ResourceUrl) && local is null && IsInternalUpload(lesson.ResourceUrl))
        {
            // Tệp nội bộ không còn trên máy chủ: để nguyên "/uploads/..." thì sang LMS khác là một trình phát / khung
            // tài liệu trắng trơn không lời giải thích. Nói thẳng cho học viên biết.
            media = "<div class=\"card offline\">Tệp đính kèm của bài học này không còn trên máy chủ lúc xuất gói, "
                    + "nên không có trong gói. Liên hệ người phụ trách đào tạo để nhận tài liệu.</div>";
        }
        else if (!string.IsNullOrWhiteSpace(lesson.ResourceUrl))
        {
            if (local is not null) fileList.Add(local);
            var src = local is not null ? "../" + local : lesson.ResourceUrl;
            var safeSrc = WebUtility.HtmlEncode(src);

            media = lesson.ContentType switch
            {
                LessonContentType.Video => $"<div class=\"card\"><video class=\"media\" controls preload=\"metadata\" src=\"{safeSrc}\"></video></div>",
                LessonContentType.Image => $"<div class=\"card\"><img class=\"media\" src=\"{safeSrc}\" alt=\"\"></div>",
                LessonContentType.Link => $"<div class=\"card\"><a class=\"btn ghost\" href=\"{safeSrc}\" target=\"_blank\" rel=\"noopener\">Mở liên kết học liệu</a></div>",
                _ when src.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) =>
                    $"<div class=\"card\"><iframe class=\"media\" src=\"{safeSrc}\" title=\"Tài liệu\"></iframe>"
                    + $"<p><a class=\"btn ghost\" href=\"{safeSrc}\" target=\"_blank\" rel=\"noopener\">Mở tài liệu ở cửa sổ mới</a></p></div>",
                _ => $"<div class=\"card\"><a class=\"btn ghost\" href=\"{safeSrc}\" target=\"_blank\" rel=\"noopener\" download>Mở / tải tài liệu đính kèm</a></div>",
            };
        }

        var body = await RewriteUploadReferencesAsync(context, lesson.Content ?? "", fileList);
        if (string.IsNullOrWhiteSpace(body)) body = "<p>Bài học này chỉ gồm tài liệu đính kèm.</p>";

        var href = $"lessons/lesson-{lesson.Id}.html";
        if (context.Archive is not null)
        {
            var html = ScormExportTemplates.LessonPage
                .Replace("{{TITLE}}", WebUtility.HtmlEncode(lesson.Title))
                .Replace("{{META}}", lesson.DurationMinutes > 0 ? $"Thời lượng dự kiến: {lesson.DurationMinutes} phút" : "")
                .Replace("{{MEDIA}}", media)
                .Replace("{{BODY}}", body);
            await AddTextAsync(context, href, html);
        }
        return new ScormResourceSpec(href, true, fileList.Distinct().ToList(), SharedResourceId);
    }

    private static readonly Regex UploadReferenceRegex = new(
        "(?<prefix>(?:src|href|poster)\\s*=\\s*[\"']|url\\(\\s*[\"']?)(?<url>/uploads/[^\"')\\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Đổi mọi tham chiếu "/uploads/..." trong HTML bài học (ảnh chèn trong Quill, ảnh/bảng nhập từ Word...)
    /// sang tệp đóng trong gói — gói mang sang LMS khác không còn máy chủ này để tải về.</summary>
    private async Task<string> RewriteUploadReferencesAsync(ExportContext context, string html, List<string> fileList)
    {
        var replacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UploadReferenceRegex.Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (replacement.ContainsKey(url)) continue;
            var local = await PackAssetAsync(context, url);
            if (local is null) continue;
            replacement[url] = "../" + local;
            if (!fileList.Contains(local)) fileList.Add(local);
        }

        return UploadReferenceRegex.Replace(html, m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups["url"].Value);
            return replacement.TryGetValue(url, out var local) ? m.Groups["prefix"].Value + local : m.Value;
        });
    }

    private static bool IsInternalUpload(string url) => url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tệp nội bộ /uploads/... còn trên đĩa không.</summary>
    private bool UploadExists(string url)
    {
        using var stream = _storage.OpenUpload(url);
        return stream is not null;
    }

    /// <summary>Đóng một tệp /uploads vào thư mục res/ của gói; trả đường dẫn trong gói, null nếu không phải tệp nội
    /// bộ hoặc tệp đã mất.</summary>
    private async Task<string?> PackAssetAsync(ExportContext context, string url)
    {
        if (context.AssetByUrl.TryGetValue(url, out var known)) return known;

        await using var source = _storage.OpenUpload(url);
        if (source is null)
        {
            context.AssetByUrl[url] = null;
            return null;
        }

        var extension = Path.GetExtension(url.Split('?', '#')[0]).ToLowerInvariant();
        if (!Regex.IsMatch(extension, "^\\.[a-z0-9]{1,8}$")) extension = "";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()[..12];
        var local = $"res/u-{hash}{extension}";

        if (context.Archive is not null)
        {
            // Video / tệp đã nén sẵn thì nén thêm chỉ tốn CPU.
            var level = extension is ".mp4" or ".webm" or ".mp3" or ".jpg" or ".jpeg" or ".png" or ".zip" or ".pdf"
                ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var entry = context.Archive.CreateEntry(local, level);
            await using var target = entry.Open();
            await source.CopyToAsync(target);
        }

        context.AssetByUrl[url] = local;
        return local;
    }

    // ==================== BÀI KIỂM TRA ====================

    private async Task<ScormResourceSpec> ExportQuizAsync(ExportContext context, int quizId)
    {
        var href = $"quizzes/quiz-{quizId}.html";
        if (context.Archive is null) return new ScormResourceSpec(href, true, new List<string>(), SharedResourceId);

        var quiz = await _db.Quiz.AsNoTracking()
            .Include(x => x.QuestionList).ThenInclude(q => q.AnswerList)
            .FirstAsync(x => x.Id == quizId);

        var data = new
        {
            passScore = quiz.PassScore,
            timeLimitMinutes = quiz.TimeLimitMinutes,
            shuffle = quiz.ShuffleQuestion,
            questions = quiz.QuestionList.OrderBy(q => q.OrderIndex).Select(q => new
            {
                id = q.Id,
                content = q.Content,
                score = q.Score,
                multiple = q.QuestionType == QuestionType.MultipleChoice,
                explanation = q.Explanation,
                answers = q.AnswerList.OrderBy(a => a.OrderIndex).Select(a => new { id = a.Id, content = a.Content, correct = a.IsCorrect }),
            }),
        };
        // "</" trong chuỗi JSON đặt giữa thẻ <script> sẽ đóng thẻ sớm — thoát thành "<\/".
        var json = JsonSerializer.Serialize(data).Replace("</", "<\\/");

        var meta = $"{quiz.QuestionList.Count} câu hỏi · Điểm đạt {quiz.PassScore:0.##}%"
                   + (quiz.TimeLimitMinutes > 0 ? $" · {quiz.TimeLimitMinutes} phút" : "");
        var html = ScormExportTemplates.QuizPage
            .Replace("{{TITLE}}", WebUtility.HtmlEncode(quiz.Title))
            .Replace("{{META}}", WebUtility.HtmlEncode(meta))
            .Replace("{{QUIZ_JSON}}", json);
        await AddTextAsync(context, href, html);
        return new ScormResourceSpec(href, true, new List<string>(), SharedResourceId);
    }

    // ==================== NHÚNG GÓI SCORM 2004 ====================

    /// <summary>Gói nhập ngoài đã nằm sẵn trong cây (cụm "P{id}", định danh đã gắn tiền tố — xem
    /// CourseOrganizationBuilder.EmbedPackage). Ở đây chỉ đóng tệp của gói vào packages/p{id}/ và trỏ từng hoạt động
    /// lá của gói về đúng tệp đó.</summary>
    private async Task EmbedPackageAsync(ExportContext context, int packageId, ScormActivityDefinition cluster,
        Dictionary<string, ScormResourceSpec> specById)
    {
        var package = await _db.ScormPackage.AsNoTracking().FirstAsync(x => x.Id == packageId);
        var folder = $"packages/p{package.Id}/";
        if (context.Archive is not null)
            await _storage.AddPackageToZipAsync(package.StorageKey, context.Archive, folder, includeManifest: false);

        var servedPrefix = $"/scorm-content/{package.StorageKey}/";
        void Walk(ScormActivityDefinition activity)
        {
            if (activity.IsLeaf && activity.LaunchUrl is string url)
            {
                var href = url.StartsWith(servedPrefix, StringComparison.Ordinal) ? folder + url[servedPrefix.Length..] : url;
                specById[activity.Id] = new ScormResourceSpec(href, activity.IsSco, new List<string>());
            }
            foreach (var child in activity.Children) Walk(child);
        }
        Walk(cluster);
    }

    private static async Task AddTextAsync(ExportContext context, string path, string content)
    {
        if (context.Archive is null) return;
        var entry = context.Archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }
}
