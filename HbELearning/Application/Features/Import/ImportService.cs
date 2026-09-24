// File: Application/Features/Import/ImportService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Import;

public class ImportService : IImportService
{
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly IDataImportService _dataImportService;
    private readonly IWordHtmlConverter _wordHtmlConverter;
    private readonly IPowerPointHtmlConverter _powerPointHtmlConverter;
    private readonly IWordQuestionParser _wordQuestionParser;
    private readonly IFileService _fileService;
    private readonly IAppDbContext _db;

    public ImportService(IDataImportService dataImportService, IWordHtmlConverter wordHtmlConverter,
        IPowerPointHtmlConverter powerPointHtmlConverter, IWordQuestionParser wordQuestionParser,
        IFileService fileService, IAppDbContext db)
    {
        _dataImportService = dataImportService;
        _wordHtmlConverter = wordHtmlConverter;
        _powerPointHtmlConverter = powerPointHtmlConverter;
        _wordQuestionParser = wordQuestionParser;
        _fileService = fileService;
        _db = db;
    }

    public Result DownloadQuestionTemplate(string? format)
    {
        var chosen = ImportFormatExtensions.FromQuery(format);
        return TemplateFile(_dataImportService.BuildQuestionTemplate(chosen), chosen, "Mau_Import_CauHoi");
    }

    public Result DownloadLessonTemplate(string? format)
    {
        var chosen = ImportFormatExtensions.FromQuery(format);
        return TemplateFile(_dataImportService.BuildLessonTemplate(chosen), chosen, "Mau_Import_BaiHoc");
    }

    /// <summary>Đóng gói file mẫu kèm đúng kiểu MIME và phần mở rộng.
    ///
    /// KHÔNG có file mẫu dạng PDF: file mẫu sinh ra để người dùng điền vào rồi tải lên lại, mà PDF thì không
    /// gõ thêm được. Muốn nhập từ PDF thì soạn nội dung bằng công cụ của bạn theo đúng cách viết ghi trong
    /// file mẫu Word (mỗi dòng một bản ghi, các cột ngăn nhau bằng dấu "|") rồi xuất ra PDF.</summary>
    private Result TemplateFile(byte[] content, ImportFormat format, string baseName) =>
        format == ImportFormat.Word
            ? Result.FileDownload(content, WordMime, baseName + ".docx")
            : Result.FileDownload(content, ExcelMime, baseName + ".xlsx");

    public async Task<Result> ImportQuestion(int quizId, FileUpload file)
    {
        var (format, error) = Validate(file);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        try
        {
            await using var stream = file.OpenReadStream();
            return Result.Success(await _dataImportService.ImportQuestionAsync(quizId, stream, format));
        }
        catch (KeyNotFoundException ex) { return Result.NotFound(new MessageResponse(ex.Message)); }
        catch (Exception ex) { return Result.Invalid(new MessageResponse($"Không đọc được file {format.ToVietnameseLabel()}: {ex.Message}")); }
    }

    public async Task<Result> ImportLesson(int courseId, FileUpload file)
    {
        var (format, error) = Validate(file);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        try
        {
            await using var stream = file.OpenReadStream();
            return Result.Success(await _dataImportService.ImportLessonAsync(courseId, stream, format));
        }
        catch (KeyNotFoundException ex) { return Result.NotFound(new MessageResponse(ex.Message)); }
        catch (Exception ex) { return Result.Invalid(new MessageResponse($"Không đọc được file {format.ToVietnameseLabel()}: {ex.Message}")); }
    }

    // ==================================================================
    //  NGÂN HÀNG CÂU HỎI SOẠN TỰ DO, GỘP NHIỀU BÀI TRONG MỘT FILE WORD
    // ==================================================================

    public async Task<Result> GetQuestionOutline(int courseId, FileUpload file)
    {
        if (ValidateQuestionDocument(file) is string error) return Result.Invalid(new MessageResponse(error));

        WordQuestionDocument document;
        await using (var stream = file.OpenReadStream()) document = _wordQuestionParser.Parse(stream);

        if (document.GroupList.Count == 0)
            return Result.Invalid(new MessageResponse(NoQuestionMessage(document)));

        return await BuildOutlineAsync(courseId, file, document.GroupList, WarningDtoList(document));
    }

    public async Task<Result> ImportQuestionDocument(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null)
    {
        if (ValidateQuestionDocument(file) is string error) return Result.Invalid(new MessageResponse(error));

        WordQuestionDocument document;
        await using (var stream = file.OpenReadStream()) document = _wordQuestionParser.Parse(stream);

        if (document.GroupList.Count == 0)
            return Result.Invalid(new MessageResponse(NoQuestionMessage(document)));

        return await CreateQuestionGroupListAsync(
            courseId, file, document.GroupList, WarningDtoList(document), sectionIndexList);
    }

    // ==================================================================
    //  NGÂN HÀNG CÂU HỎI CẢ KHÓA HỌC SOẠN THEO FILE MẪU (BẢNG EXCEL)
    // ==================================================================

    public Result DownloadQuestionSheetTemplate(string? format)
    {
        var chosen = ImportFormatExtensions.FromQuery(format);
        return TemplateFile(_dataImportService.BuildQuestionSheetTemplate(chosen), chosen, "Mau_Import_CauHoi_CaKhoaHoc");
    }

    public async Task<Result> GetQuestionSheetOutline(int courseId, FileUpload file)
    {
        var (sheet, error) = ReadQuestionSheet(file);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        return await BuildOutlineAsync(courseId, file, sheet!.GroupList, SheetErrorDtoList(sheet));
    }

    public async Task<Result> ImportQuestionSheet(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null)
    {
        var (sheet, error) = ReadQuestionSheet(file);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        return await CreateQuestionGroupListAsync(
            courseId, file, sheet!.GroupList, SheetErrorDtoList(sheet), sectionIndexList);
    }

    /// <summary>Đọc bảng câu hỏi cả khóa học, trả về lý do bằng tiếng Việt nếu file không dùng được.</summary>
    private (QuestionSheet? Sheet, string? Error) ReadQuestionSheet(FileUpload file)
    {
        var (format, error) = Validate(file);
        if (error is not null) return (null, error);

        QuestionSheet sheet;
        try
        {
            using var stream = file.OpenReadStream();
            sheet = _dataImportService.ReadQuestionSheet(stream, format);
        }
        catch (Exception ex)
        {
            return (null, $"Không đọc được file {format.ToVietnameseLabel()}: {ex.Message}");
        }

        if (sheet.GroupList.Count == 0)
            return (null, sheet.ErrorList.Count > 0
                ? "Không đọc được câu hỏi nào. Dòng đầu tiên báo: " + sheet.ErrorList[0].Message
                : "File không có dòng câu hỏi nào. Hãy tải file mẫu, điền theo đúng các cột trong đó rồi nhập lại.");

        return (sheet, null);
    }

    private static List<ImportErrorDto> SheetErrorDtoList(QuestionSheet sheet) =>
        sheet.ErrorList.Select(x => new ImportErrorDto(x.Line, x.Message)).ToList();

    // ==================================================================
    //  PHẦN DÙNG CHUNG CỦA HAI LỐI NHẬP TRÊN
    // ==================================================================

    /// <summary>Xem trước: từng nhóm câu hỏi trong file sẽ đổ vào bài kiểm tra nào. KHÔNG ghi gì.</summary>
    private async Task<Result> BuildOutlineAsync(
        int courseId, FileUpload file, IReadOnlyList<ParsedQuestionGroup> groupList, List<ImportErrorDto> warningList)
    {
        var targetList = await ResolveTargetListAsync(courseId, groupList, file);

        var sectionList = targetList
            .Select((x, index) => new QuestionSectionDto(
                index, x.Title, x.Group.QuestionList.Count, x.QuizTitle, x.Quiz is null, x.LessonTitle,
                x.Group.QuestionList.Count(q => q.NeedsAnswerReview)))
            .ToList();

        return Result.Success(new QuestionOutlineDto(sectionList, warningList));
    }

    /// <summary>Ghi thật: tạo bài kiểm tra còn thiếu rồi đổ câu hỏi của những nhóm người soạn đã chọn.</summary>
    private async Task<Result> CreateQuestionGroupListAsync(
        int courseId, FileUpload file, IReadOnlyList<ParsedQuestionGroup> groupList,
        List<ImportErrorDto> warningList, IReadOnlyList<int>? sectionIndexList)
    {
        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var targetList = await ResolveTargetListAsync(courseId, groupList, file);
        var chosenList = targetList
            .Where((_, index) => sectionIndexList is null || sectionIndexList.Count == 0 || sectionIndexList.Contains(index))
            .ToList();

        if (chosenList.Count == 0)
            return Result.Invalid(new MessageResponse("Không có nhóm câu hỏi nào được chọn."));

        var createdQuizCount = 0;
        var importedCount = 0;

        foreach (var target in chosenList)
        {
            var quiz = target.Quiz;
            if (quiz is null)
            {
                // Bài kiểm tra mới lấy điểm đạt của chính khóa học, để không phải sửa tay từng bài sau khi nhập.
                quiz = new Quiz
                {
                    CourseId = courseId,
                    LessonId = target.LessonId,
                    Title = Shorten(target.QuizTitle, 250),
                    PassScore = course.PassScore,
                    IsActive = true
                };
                _db.Quiz.Add(quiz);
                await _db.SaveChangesAsync();
                createdQuizCount++;
            }

            importedCount += await AddQuestionListAsync(quiz.Id, target.Group.QuestionList);
        }

        await _db.SaveChangesAsync();

        var reviewCount = chosenList.Sum(x => x.Group.QuestionList.Count(q => q.NeedsAnswerReview));
        return Result.Success(new QuestionDocumentResultDto(
            importedCount + warningList.Count, importedCount, warningList.Count, warningList,
            createdQuizCount, chosenList.Count, reviewCount));
    }

    /// <summary>Nhập một file Word đề thi tự do vào ĐÚNG MỘT bài kiểm tra — dùng từ hộp thoại Ngân hàng câu
    /// hỏi của chính bài đó. Dòng chia bài trong file (nếu có) bị bỏ qua: người soạn đã chỉ rõ đích đến rồi.</summary>
    public async Task<Result> ImportQuestionDocumentToQuiz(int quizId, FileUpload file)
    {
        if (ValidateQuestionDocument(file) is string error) return Result.Invalid(new MessageResponse(error));

        var quiz = await _db.Quiz.FirstOrDefaultAsync(x => x.Id == quizId);
        if (quiz is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài kiểm tra."));

        WordQuestionDocument document;
        await using (var stream = file.OpenReadStream()) document = _wordQuestionParser.Parse(stream);

        var questionList = document.GroupList.SelectMany(x => x.QuestionList).ToList();
        if (questionList.Count == 0)
            return Result.Invalid(new MessageResponse(NoQuestionMessage(document)));

        var importedCount = await AddQuestionListAsync(quizId, questionList);
        await _db.SaveChangesAsync();

        var warningList = WarningDtoList(document);
        return Result.Success(new QuestionDocumentResultDto(
            importedCount + warningList.Count, importedCount, warningList.Count, warningList, 0, 1,
            questionList.Count(x => x.NeedsAnswerReview)));
    }

    /// <summary>Ghi các câu hỏi đã bóc tách vào một bài kiểm tra, xếp tiếp sau những câu đã có.</summary>
    private async Task<int> AddQuestionListAsync(int quizId, IReadOnlyList<ParsedQuestion> questionList)
    {
        var nextOrder = (await _db.Question.Where(x => x.QuizId == quizId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0) + 1;

        foreach (var parsed in questionList)
        {
            var question = new Question
            {
                QuizId = quizId,
                Content = Shorten(parsed.Content, 2000),
                QuestionType = QuestionTypeOf(parsed),
                Score = parsed.Score is > 0 ? parsed.Score.Value : 1m,
                OrderIndex = nextOrder++,
                Explanation = parsed.Explanation is null ? null : Shorten(parsed.Explanation, 1000)
            };

            var answerOrder = 1;
            foreach (var answer in parsed.AnswerList)
                question.AnswerList.Add(new Answer
                {
                    Content = Shorten(answer.Content, 1000),
                    IsCorrect = answer.IsCorrect,
                    OrderIndex = answerOrder++
                });

            _db.Question.Add(question);
        }

        return questionList.Count;
    }

    /// <summary>Một nhóm câu hỏi trong file và bài kiểm tra sẽ nhận nó.</summary>
    /// <param name="Quiz">Bài kiểm tra đã có; null = sẽ tạo mới mang tên <paramref name="QuizTitle"/>.</param>
    private sealed record QuestionTarget(
        ParsedQuestionGroup Group, string Title, Quiz? Quiz, string QuizTitle, int? LessonId, string? LessonTitle);

    /// <summary>
    /// Ghép từng nhóm câu hỏi vào bài kiểm tra của đúng bài đó: khớp tên với bài kiểm tra đã có, không có thì
    /// khớp với BÀI HỌC cùng tên (tạo bài kiểm tra gắn vào bài học đó), cuối cùng mới tạo một bài kiểm tra
    /// độc lập. So tên bỏ qua hoa thường, dấu tiếng Việt và dấu câu, nên "BÀI 1: AN TOÀN ĐIỆN" trong file vẫn
    /// khớp "Bài 1 - An toàn điện" trong hệ thống; không khớp cả cụm thì khớp theo số bài ("bài 1").
    /// </summary>
    private async Task<List<QuestionTarget>> ResolveTargetListAsync(
        int courseId, IReadOnlyList<ParsedQuestionGroup> groupList, FileUpload file)
    {
        var quizList = await _db.Quiz.Where(x => x.CourseId == courseId).ToListAsync();
        var lessonList = await _db.Lesson.Where(x => x.CourseId == courseId && x.IsActive).ToListAsync();

        var fallbackTitle = Path.GetFileNameWithoutExtension(file.FileName).Trim();
        if (fallbackTitle.Length == 0) fallbackTitle = "Câu hỏi nhập từ file";

        var resultList = new List<QuestionTarget>();
        foreach (var group in groupList)
        {
            var title = string.IsNullOrWhiteSpace(group.Title) ? fallbackTitle : group.Title!;

            var quiz = quizList.FirstOrDefault(x => TitleKey(x.Title) == TitleKey(title))
                       ?? quizList.FirstOrDefault(x => LessonNumberKey(title) is string key && LessonNumberKey(x.Title) == key);
            if (quiz is not null)
            {
                var ownerTitle = lessonList.FirstOrDefault(x => x.Id == quiz.LessonId)?.Title;
                resultList.Add(new QuestionTarget(group, title, quiz, quiz.Title, quiz.LessonId, ownerTitle));
                continue;
            }

            var lesson = lessonList.FirstOrDefault(x => TitleKey(x.Title) == TitleKey(title))
                         ?? lessonList.FirstOrDefault(x => LessonNumberKey(title) is string key && LessonNumberKey(x.Title) == key);

            resultList.Add(new QuestionTarget(
                group, title, null, lesson?.Title ?? title, lesson?.Id, lesson?.Title));
        }

        return resultList;
    }

    /// <summary>Kiểu câu hỏi: lấy đúng thứ người soạn ghi ở cột "Kiểu câu hỏi" nếu có, còn file Word soạn tự
    /// do không có cột ấy thì suy ra từ số đáp án đúng và nội dung đáp án.</summary>
    private static QuestionType QuestionTypeOf(ParsedQuestion question)
    {
        if (question.DeclaredType is QuestionType declared) return declared;

        if (question.AnswerList.Count(x => x.IsCorrect) > 1) return QuestionType.MultipleChoice;

        var isTrueFalse = question.AnswerList.Count == 2
                          && question.AnswerList.All(x => TitleKey(x.Content) is "dung" or "sai" or "true" or "false");
        return isTrueFalse ? QuestionType.TrueFalse : QuestionType.SingleChoice;
    }

    private static string? ValidateQuestionDocument(FileUpload? file)
    {
        if (file is null || file.Length == 0) return "Chưa chọn file.";
        return Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Chỉ hỗ trợ file Word (.docx). File .doc cũ hãy mở bằng Word và lưu lại dạng .docx.";
    }

    private static string NoQuestionMessage(WordQuestionDocument document) =>
        document.WarningList.Count > 0
            ? "Không đọc được câu hỏi nào. " + document.WarningList[0].Message
            : "Không tìm thấy câu hỏi nào trong file. Mỗi câu cần có nội dung, ít nhất 2 đáp án (A, B...) "
              + "và một cách đánh dấu đáp án đúng (dòng \"Đáp án: A\", in đậm, hoặc dấu * ở đầu đáp án).";

    private static List<ImportErrorDto> WarningDtoList(WordQuestionDocument document) =>
        document.WarningList.Select(x => new ImportErrorDto(x.Line, x.Message)).ToList();

    private static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength].TrimEnd();

    /// <summary>Khóa so tên: bỏ hoa thường, bỏ dấu tiếng Việt, bỏ dấu câu, gộp khoảng trắng. "BÀI 1: AN TOÀN
    /// ĐIỆN" và "Bài 1 - An toàn điện" ra cùng một khóa.</summary>
    private static string TitleKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var normalized = title.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        var previousWasSpace = false;

        foreach (var character in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

            // đ/Đ không phải chữ d kèm dấu nên tách dấu không đụng tới nó — quy tay về "d".
            var letter = character is 'đ' ? 'd' : character;

            if (char.IsLetterOrDigit(letter))
            {
                builder.Append(letter);
                previousWasSpace = false;
                continue;
            }

            if (builder.Length > 0 && !previousWasSpace) builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    /// <summary>Phần "bài + số" của một tên, để khớp khi phần chữ phía sau khác nhau ("Bài 1: An toàn điện"
    /// với "Bài 1. Kiến thức an toàn điện"). null nếu tên không mở đầu bằng bài/chương/phần + số.</summary>
    private static string? LessonNumberKey(string? title)
    {
        var key = TitleKey(title);
        var match = System.Text.RegularExpressions.Regex.Match(key, @"^(bai|chuong|phan|chuyen de)\s*(\d{1,3})\b");
        return match.Success ? match.Groups[1].Value + " " + match.Groups[2].Value : null;
    }

    public async Task<Result> GetDocumentOutline(FileUpload file)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn file."));
        if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return Result.Invalid(new MessageResponse("Chỉ file Word (.docx) mới tách được thành nhiều bài học."));

        try
        {
            await using var stream = file.OpenReadStream();
            // Trả về chuỗi rỗng ở chỗ lưu ảnh: đây mới là lượt XEM TRƯỚC, người soạn có thể bấm Hủy. Lưu ảnh
            // ngay từ bước này thì mỗi lần hủy lại bỏ lại một đống ảnh mồ côi trong thư mục tải lên.
            var result = await _wordHtmlConverter.ConvertSectionsAsync(stream, (_, _) => Task.FromResult(string.Empty));

            var sectionList = result.SectionList
                .Select((x, index) => new DocumentSectionDto(
                    index, SectionTitle(x, file, result.SplitRule != "none"), x.CharacterCount, x.ImageCount))
                .ToList();

            return Result.Success(new DocumentOutlineDto(result.SplitRule, sectionList));
        }
        catch (Exception ex)
        {
            return Result.Invalid(new MessageResponse($"Không đọc được tài liệu: {ex.Message}"));
        }
    }

    public async Task<Result> ImportDocument(int courseId, FileUpload file, IReadOnlyList<int>? sectionIndexList = null)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn file."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".docx" or ".pdf" or ".pptx"))
            return Result.Invalid(new MessageResponse(
                "Chỉ hỗ trợ .docx, .pptx và .pdf. File .doc / .ppt (Office 97-2003) hãy mở bằng Word / PowerPoint "
                + "và lưu lại dạng .docx / .pptx."));

        var course = await _db.Course.FirstOrDefaultAsync(x => x.Id == courseId);
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        // Bài học mới luôn xếp cuối khóa.
        var nextOrder = (await _db.Lesson.Where(x => x.CourseId == courseId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0) + 1;

        var fallbackTitle = Path.GetFileNameWithoutExtension(file.FileName).Trim();
        if (fallbackTitle.Length == 0) fallbackTitle = "Tài liệu nhập từ file";

        try
        {
            if (extension == ".docx")
                return await ImportWordAsync(courseId, file, nextOrder, sectionIndexList);

            var lesson = extension == ".pdf"
                ? await BuildPdfLessonAsync(file, courseId, nextOrder, fallbackTitle)
                : await BuildPowerPointLessonAsync(file, courseId, nextOrder, fallbackTitle);

            _db.Lesson.Add(lesson.Lesson);
            await _db.SaveChangesAsync();

            return Result.Success(new DocumentImportResultDto(
                lesson.Lesson.Id, lesson.Lesson.Title, lesson.Mode, lesson.ImageCount,
                lesson.Lesson.ResourceUrl, lesson.Message));
        }
        catch (ArgumentException ex) { return Result.Invalid(new MessageResponse(ex.Message)); }
        catch (Exception ex) { return Result.Invalid(new MessageResponse($"Không đọc được tài liệu: {ex.Message}")); }
    }

    /// <summary>
    /// Word: một file có thể thành NHIỀU bài học. Bộ chuyển tự tách theo dòng tiêu đề chia bài (xem
    /// IWordHtmlConverter.ConvertSectionsAsync); <paramref name="sectionIndexList"/> là những phần người soạn
    /// đã tick trong hộp thoại xem trước — bỏ trống thì lấy hết.
    ///
    /// Tách được đúng một phần thì kết quả y hệt cách nhập cũ: một bài học cho cả tài liệu.
    /// </summary>
    private async Task<Result> ImportWordAsync(
        int courseId, FileUpload file, int nextOrder, IReadOnlyList<int>? sectionIndexList)
    {
        WordSectionResult result;
        await using (var stream = file.OpenReadStream())
        {
            result = await _wordHtmlConverter.ConvertSectionsAsync(stream, async (data, imageExtension) =>
            {
                var image = new FileUpload($"anh{imageExtension}", data.Length,
                    "image/" + imageExtension.TrimStart('.'), () => new MemoryStream(data));
                var saved = await _fileService.SaveAsync(image, "lesson");
                return saved.Url;
            });
        }

        if (result.SectionList.Count == 0)
            return Result.Invalid(new MessageResponse("Tài liệu Word không có nội dung nào đọc được."));

        // Số thứ tự phần do chính bước xem trước gửi lên; số lạ (file đã bị đổi giữa hai bước) thì bỏ qua.
        var chosenList = result.SectionList
            .Select((Section, Index) => (Section, Index))
            .Where(x => sectionIndexList is null || sectionIndexList.Count == 0 || sectionIndexList.Contains(x.Index))
            .ToList();

        if (chosenList.Count == 0)
            return Result.Invalid(new MessageResponse("Không có phần nào được chọn để tạo bài học."));

        var lessonList = chosenList.Select((x, order) => new Lesson
        {
            CourseId = courseId,
            Title = SectionTitle(x.Section, file, result.SectionList.Count > 1),
            ContentType = LessonContentType.Text,
            Content = x.Section.Html,
            OrderIndex = nextOrder + order,
            DurationMinutes = 0,
            IsActive = true
        }).ToList();

        _db.Lesson.AddRange(lessonList);
        await _db.SaveChangesAsync();

        var message = lessonList.Count == 1
            ? "Đã dựng lại nội dung Word thành bài học"
              + (result.ImageCount > 0 ? $", kèm {result.ImageCount} ảnh." : ".")
            : $"Đã tách file Word thành {lessonList.Count} bài học"
              + (result.ImageCount > 0 ? $", kèm {result.ImageCount} ảnh." : ".");

        return Result.Success(new DocumentImportResultDto(
            lessonList[0].Id, lessonList[0].Title, lessonList.Count > 1 ? "word-split" : "word-html",
            result.ImageCount, null, message,
            lessonList.Count, lessonList.Select(x => x.Title).ToList()));
    }

    /// <summary>Tên bài học của một phần: dòng tiêu đề mở đầu phần đó. Phần nằm TRƯỚC dòng tiêu đề đầu tiên
    /// (trang bìa, mục lục, lời nói đầu) không có tên nên đặt là "Mở đầu"; tài liệu không tách được thì lấy
    /// tên file như cách nhập cũ. Cắt theo giới hạn 250 ký tự của cột tên bài học.</summary>
    private static string SectionTitle(WordSection section, FileUpload file, bool isSplit)
    {
        var fallback = Path.GetFileNameWithoutExtension(file.FileName).Trim();
        if (fallback.Length == 0) fallback = "Tài liệu nhập từ file";

        var title = string.IsNullOrWhiteSpace(section.Title)
            ? (isSplit ? "Mở đầu" : fallback)
            : section.Title!.Trim();

        return title.Length > 250 ? title[..250].TrimEnd() : title;
    }

    /// <summary>PDF: lưu nguyên file, bài học chỉ trỏ tới nó — trang học tự nhúng khung xem PDF vào.</summary>
    private async Task<(Lesson Lesson, string Mode, int ImageCount, string Message)> BuildPdfLessonAsync(
        FileUpload file, int courseId, int order, string title)
    {
        var saved = await _fileService.SaveAsync(file, "lesson");

        return (new Lesson
        {
            CourseId = courseId,
            Title = title,
            ContentType = LessonContentType.Document,
            ResourceUrl = saved.Url,
            OrderIndex = order,
            DurationMinutes = 0,
            IsActive = true
        }, "pdf-embed", 0, "Đã nhúng nguyên file PDF vào bài học — học viên xem đúng bản gốc.");
    }

    /// <summary>PowerPoint: dựng từng slide thành HTML để học viên xem ngay trong trang học, đồng thời lưu
    /// nguyên file gốc làm tài liệu đính kèm — trang học hiện nút tải về, ai cần trình chiếu lại hay xem hiệu
    /// ứng chuyển slide vẫn có bản gốc.
    ///
    /// Dựng HTML TRƯỚC rồi mới lưu file gốc: file hỏng hoặc không có slide nào thì dừng luôn, không để lại
    /// một file mồ côi trong thư mục tải lên.</summary>
    private async Task<(Lesson Lesson, string Mode, int ImageCount, string Message)> BuildPowerPointLessonAsync(
        FileUpload file, int courseId, int order, string fallbackTitle)
    {
        PowerPointHtmlResult result;
        await using (var stream = file.OpenReadStream())
        {
            result = await _powerPointHtmlConverter.ConvertAsync(stream, async (data, imageExtension) =>
            {
                var image = new FileUpload($"anh{imageExtension}", data.Length,
                    "image/" + imageExtension.TrimStart('.'), () => new MemoryStream(data));
                var saved = await _fileService.SaveAsync(image, "lesson");
                return saved.Url;
            });
        }

        if (result.SlideCount == 0 || string.IsNullOrWhiteSpace(result.Html))
            throw new ArgumentException("File PowerPoint không có slide nào hiển thị được (mọi slide đều trống hoặc bị ẩn).");

        var original = await _fileService.SaveAsync(file, "lesson");

        var message = $"Đã dựng {result.SlideCount} slide thành bài học"
                      + (result.ImageCount > 0 ? $", kèm {result.ImageCount} ảnh" : "")
                      + ". File PowerPoint gốc được đính kèm để học viên tải về.";
        if (result.SkippedObjectCount > 0)
            message += $" Có {result.SkippedObjectCount} đối tượng (biểu đồ, SmartArt, ảnh định dạng lạ...) không hiển thị được — "
                       + "hãy xem lại các slide đó.";

        return (new Lesson
        {
            CourseId = courseId,
            Title = string.IsNullOrWhiteSpace(result.Title) ? fallbackTitle : result.Title!,
            ContentType = LessonContentType.Text,
            Content = result.Html,
            ResourceUrl = original.Url,
            OrderIndex = order,
            DurationMinutes = 0,
            IsActive = true
        }, "pptx-slides", result.ImageCount, message);
    }

    /// <summary>Định dạng suy ra từ phần mở rộng của chính file tải lên, không nhận từ tham số riêng: người
    /// dùng chọn nhầm mục trong menu rồi lại tải lên file khác kiểu thì hệ thống vẫn đọc đúng.</summary>
    private static (ImportFormat Format, string? Error) Validate(FileUpload? file)
    {
        if (file is null || file.Length == 0)
            return (ImportFormat.Excel, "Chưa chọn file.");

        var format = ImportFormatExtensions.FromFileName(file.FileName);
        if (format is null)
            return (ImportFormat.Excel,
                "Chỉ hỗ trợ .xlsx (Excel), .docx (Word) và .pdf. File .xls/.csv hãy mở bằng Excel và lưu lại "
                + "dạng .xlsx; file .doc hãy mở bằng Word và lưu lại dạng .docx.");

        return (format.Value, null);
    }
}
