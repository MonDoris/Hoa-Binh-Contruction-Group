// File: Application/Features/Lessons/LessonService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Lessons;

public class LessonService : ILessonService
{
    private readonly IAppDbContext _db;
    private readonly ILearningService _learningService;
    private readonly ICurrentUser _currentUser;
    private readonly IHtmlSanitizer _htmlSanitizer;
    private readonly ILearningPathService _learningPathService;

    public LessonService(IAppDbContext db, ILearningService learningService, ICurrentUser currentUser,
        IHtmlSanitizer htmlSanitizer, ILearningPathService learningPathService)
    {
        _db = db;
        _learningService = learningService;
        _currentUser = currentUser;
        _htmlSanitizer = htmlSanitizer;
        _learningPathService = learningPathService;
    }

    public async Task<Result> GetByCourse(int courseId)
    {
        // Trưởng phòng/Giám Đốc duyệt nội dung nên phải thấy đúng những gì Trưởng Nhóm đã soạn, kể cả bài
        // đang ẩn — nếu lọc bớt thì họ duyệt một khóa học khác với khóa sẽ phát hành.
        var canSeeHidden = _currentUser.IsContentViewer;

        var raw = await _db.Lesson.AsNoTracking()
            .Where(x => x.CourseId == courseId && (canSeeHidden || x.IsActive))
            .OrderBy(x => x.OrderIndex)
            .Select(x => new
            {
                x.Id, x.CourseId, x.Title, x.ContentType, x.ResourceUrl, x.DurationMinutes, x.OrderIndex, x.IsActive,
                Quiz = x.QuizList.Where(q => q.IsActive).Select(q => new { q.Id, q.Title }).FirstOrDefault(),
                Scenario = x.ScenarioSceneList.Where(s => s.IsActive)
                    .Select(s => new { s.Id, s.Title, NodeCount = s.NodeList.Count }).FirstOrDefault()
            })
            .ToListAsync();

        var list = raw.Select(x => new LessonListDto(x.Id, x.CourseId, x.Title,
            x.ContentType.ToString(), x.ResourceUrl, x.DurationMinutes, x.OrderIndex, x.IsActive,
            x.Quiz?.Id, x.Quiz?.Title, x.Scenario?.Id, x.Scenario?.Title, x.Scenario?.NodeCount ?? 0)).ToList();

        return Result.Success(list);
    }

    public async Task<Result> GetById(int id)
    {
        var lesson = await _db.Lesson.AsNoTracking()
            .Include(x => x.Course)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));

        // Learner chỉ xem được bài học của khóa mình đã được phân công, và bài học phải đang mở theo lộ trình.
        // Trưởng phòng/Giám Đốc KHÔNG đi vào nhánh này: họ không có bản ghi phân công học nên trước đây luôn
        // bị Forbid, tức là không mở nổi nội dung bài học mà chính họ phải duyệt.
        if (!_currentUser.IsContentViewer)
        {
            var enrollmentId = await _db.Enrollment
                .Where(e => e.UserId == _currentUser.UserId && e.CourseId == lesson.CourseId)
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync();
            if (enrollmentId is null) return Result.Forbidden();

            var pathList = await _learningService.BuildPathProgressAsync(enrollmentId.Value);
            var step = pathList.FirstOrDefault(x => x.LessonId == id);
            if (step is not null && !step.IsUnlocked) return Result.Forbidden();
        }

        return Result.Success(new LessonDetailDto(lesson.Id, lesson.CourseId, lesson.Course.Title, lesson.Title,
            lesson.Content, lesson.ContentType.ToString(), lesson.ResourceUrl,
            lesson.DurationMinutes, lesson.OrderIndex, lesson.IsActive, lesson.VideoSeekPolicy.ToString()));
    }

    public async Task<Result> Create(LessonRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == request.CourseId))
            return Result.Invalid(new MessageResponse("Khóa học không tồn tại."));

        var maxOrder = await _db.Lesson.Where(x => x.CourseId == request.CourseId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var lesson = new Lesson
        {
            CourseId = request.CourseId,
            Title = request.Title.Trim(),
            // Nội dung bài học được đổ thẳng vào trang học bằng innerHTML để giữ bảng biểu của tài liệu
            // nhập từ Word, nên thẻ chạy được mã phải bị cắt TẠI ĐÂY — trước khi vào database, chứ không
            // phải lúc hiển thị: cùng một nội dung được hiện ở nhiều trang, sót một chỗ là hở cả hệ thống.
            Content = _htmlSanitizer.Sanitize(request.Content),
            ContentType = request.ContentType,
            ResourceUrl = request.ResourceUrl,
            DurationMinutes = request.DurationMinutes,
            VideoSeekPolicy = NormalizeSeekPolicy(request),
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            IsActive = request.IsActive
        };

        _db.Lesson.Add(lesson);
        await _db.SaveChangesAsync();
        await _learningPathService.AppendResourceAsync(lesson.CourseId, PathItemType.Lesson, lesson.Id, request.ParentItemId);

        return Result.Success(new MessageResponse($"Đã tạo bài học #{lesson.Id}.", lesson.Id));
    }

    public async Task<Result> CreateWithQuiz(LessonWithQuizRequest request)
    {
        if (!await _db.Course.AnyAsync(x => x.Id == request.Lesson.CourseId))
            return Result.Invalid(new MessageResponse("Khóa học không tồn tại."));

        var maxOrder = await _db.Lesson.Where(x => x.CourseId == request.Lesson.CourseId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var lesson = new Lesson
        {
            CourseId = request.Lesson.CourseId,
            Title = request.Lesson.Title.Trim(),
            Content = _htmlSanitizer.Sanitize(request.Lesson.Content),
            ContentType = request.Lesson.ContentType,
            ResourceUrl = request.Lesson.ResourceUrl,
            DurationMinutes = request.Lesson.DurationMinutes,
            VideoSeekPolicy = NormalizeSeekPolicy(request.Lesson),
            OrderIndex = request.Lesson.OrderIndex > 0 ? request.Lesson.OrderIndex : maxOrder + 1,
            IsActive = request.Lesson.IsActive
        };
        _db.Lesson.Add(lesson);
        await _db.SaveChangesAsync();

        int? quizId = null;
        if (request.Quiz is not null)
        {
            var quiz = new Quiz
            {
                CourseId = request.Lesson.CourseId,
                LessonId = lesson.Id,
                Title = request.Quiz.Title.Trim(),
                PassScore = request.Quiz.PassScore,
                TimeLimitMinutes = request.Quiz.TimeLimitMinutes,
                MaxAttempt = request.Quiz.MaxAttempt,
                IsFinalExam = false,
                ShuffleQuestion = true,
                IsActive = true
            };
            _db.Quiz.Add(quiz);
            await _db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        // Bài học thành một SCO của cấu trúc khóa học; bài kiểm tra kèm theo tự đứng ngay sau nó.
        await _learningPathService.AppendResourceAsync(lesson.CourseId, PathItemType.Lesson, lesson.Id, request.Lesson.ParentItemId);
        if (quizId.HasValue) await _learningPathService.AppendResourceAsync(lesson.CourseId, PathItemType.Quiz, quizId.Value);

        var message = quizId.HasValue
            ? $"Đã tạo bài học #{lesson.Id} kèm bài kiểm tra #{quizId}."
            : $"Đã tạo bài học #{lesson.Id}.";
        return Result.Success(new LessonWithQuizResponse(lesson.Id, quizId, message));
    }

    public async Task<Result> Update(int id, LessonRequest request)
    {
        var lesson = await _db.Lesson.FirstOrDefaultAsync(x => x.Id == id);
        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));

        lesson.Title = request.Title.Trim();
        lesson.Content = _htmlSanitizer.Sanitize(request.Content);
        lesson.ContentType = request.ContentType;
        lesson.ResourceUrl = request.ResourceUrl;
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.VideoSeekPolicy = NormalizeSeekPolicy(request);
        lesson.OrderIndex = request.OrderIndex;
        lesson.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    /// <summary>Cách tua chỉ có ý nghĩa với bài học Video. Bài học văn bản/tài liệu mà lưu "bắt buộc xem hết"
    /// là dữ liệu rác: đổi loại nội dung sang Video về sau sẽ bất ngờ khóa tua mà người soạn không hề chọn.</summary>
    private static VideoSeekPolicy NormalizeSeekPolicy(LessonRequest request)
        => request.ContentType == LessonContentType.Video ? request.VideoSeekPolicy : VideoSeekPolicy.Free;

    public async Task<Result> Reorder(List<ReorderItem> itemList)
    {
        var idList = itemList.Select(x => x.Id).ToList();
        var lessonList = await _db.Lesson.Where(x => idList.Contains(x.Id)).ToListAsync();

        foreach (var lesson in lessonList)
            lesson.OrderIndex = itemList.First(x => x.Id == lesson.Id).OrderIndex;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã cập nhật thứ tự bài học."));
    }

    public async Task<Result> Delete(int id)
    {
        var lesson = await _db.Lesson.FirstOrDefaultAsync(x => x.Id == id);
        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));

        // Đã có người học -> vô hiệu hóa thay vì xóa để giữ lịch sử
        if (await _db.LessonProgress.AnyAsync(x => x.LessonId == id))
        {
            lesson.IsActive = false;
            await _db.SaveChangesAsync();
            return Result.Success(new MessageResponse("Bài học đã có dữ liệu học tập nên được ẩn thay vì xóa."));
        }

        var pathItemList = await _db.PathItem.Where(x => x.LessonId == id).ToListAsync();
        _db.PathItem.RemoveRange(pathItemList);

        // Gỡ mọi thứ đang GẮN THEO bài học này trước khi xóa: bài kiểm tra, mô phỏng 3D và các đoạn tài
        // liệu AI. Chúng không bị xóa mà chỉ tách khỏi bài học — vẫn thuộc khóa học và dùng độc lập được.
        //
        // Trước đây CSDL tự làm việc này bằng ON DELETE SET NULL, nhưng các khóa ngoại đó đã phải chuyển
        // sang NoAction để SQL Server chấp nhận schema (xem AppDbContext). Bỏ đoạn dưới đây là xóa bài học
        // sẽ lỗi khóa ngoại.
        var quizList = await _db.Quiz.Where(x => x.LessonId == id).ToListAsync();
        foreach (var quiz in quizList) quiz.LessonId = null;

        var scenarioList = await _db.ScenarioScene.Where(x => x.LessonId == id).ToListAsync();
        foreach (var scenario in scenarioList) scenario.LessonId = null;

        // Đoạn do MÁY nạp từ chính nội dung bài học này thì XÓA hẳn, không tách ra: bài học không còn thì
        // nội dung của nó cũng hết hiệu lực, mà để lại là HB Minerva tiếp tục trả lời bằng một bài đã bị gỡ —
        // hỏng hơn hẳn việc thiếu thông tin, vì người hỏi không có cách nào biết. Lượt đồng bộ kế tiếp cũng
        // dọn được chúng (xem CourseKnowledgeService), nhưng chờ tới đó là chờ hàng giờ.
        var syncedChunkList = await _db.DocumentChunk
            .Where(x => x.LessonId == id && x.SourceKind != KnowledgeSourceKind.UploadedDocument)
            .ToListAsync();
        _db.DocumentChunk.RemoveRange(syncedChunkList);

        // Tài liệu Admin TẢI LÊN thì chỉ tách khỏi bài học: nó là file riêng, vẫn thuộc khóa học và dùng độc lập được.
        var chunkList = await _db.DocumentChunk
            .Where(x => x.LessonId == id && x.SourceKind == KnowledgeSourceKind.UploadedDocument)
            .ToListAsync();
        foreach (var chunk in chunkList) chunk.LessonId = null;

        // Ghi chú theo mốc thời gian thì XÓA hẳn chứ không tách ra: một ghi chú không còn video để bấm về
        // chỉ là dòng chữ treo lơ lửng. Phải xóa tay vì khóa ngoại tới bài học để NoAction — SQL Server
        // không nhận hai đường xóa dây chuyền cùng đổ về một bảng (xem AppDbContext).
        var noteList = await _db.LessonNote.Where(x => x.LessonId == id).ToListAsync();
        _db.LessonNote.RemoveRange(noteList);

        _db.Lesson.Remove(lesson);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã xóa bài học."));
    }
}
