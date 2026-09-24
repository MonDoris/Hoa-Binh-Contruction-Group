// File: Application/Features/VideoLearnings/VideoLearningService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.VideoLearnings;

/// <summary>Câu hỏi chèn giữa video bài học ("video cắt khúc"). Khác QuizService ở chỗ mỗi câu hỏi được
/// chấm ngay khi học viên bấm gửi (không gom thành một lượt làm bài), vì video phải chạy tiếp ngay sau đó.
/// Đáp án đúng không bao giờ đi kèm dữ liệu trả cho học viên — chỉ lộ ra sau khi đã chấm xong.</summary>
public class VideoLearningService : IVideoLearningService
{
    private readonly IAppDbContext _db;
    private readonly IRankingEngine _rankingService;
    private readonly ICurrentUser _currentUser;

    public VideoLearningService(IAppDbContext db, IRankingEngine rankingService, ICurrentUser currentUser)
    {
        _db = db;
        _rankingService = rankingService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetForManage(int lessonId)
    {
        var questionList = await _db.VideoQuestion.AsNoTracking()
            .Include(x => x.OptionList)
            .Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.AtSecond).ThenBy(x => x.OrderIndex)
            .ToListAsync();

        var idList = questionList.Select(x => x.Id).ToList();
        // Kèm cả số lượt "bị cho qua vì hết lượt trả lời": đây là tín hiệu để người soạn bài biết câu nào
        // đang quá khó so với số lần cho phép, cần sửa lại nội dung hoặc nới thêm lượt.
        var statMap = await _db.VideoQuestionAnswer.AsNoTracking()
            .Where(x => idList.Contains(x.VideoQuestionId))
            .GroupBy(x => x.VideoQuestionId)
            .Select(g => new { g.Key, Count = g.Count(), ForcedPassCount = g.Count(x => x.IsForcedPass) })
            .ToDictionaryAsync(x => x.Key, x => x);

        var list = questionList.Select(x =>
        {
            statMap.TryGetValue(x.Id, out var stat);
            return new VideoQuestionManageDto(
                x.Id, x.LessonId, x.AtSecond, x.Content, x.QuestionType.ToString(), x.Hint,
                x.MustAnswerCorrectly, x.BlockSeek, x.MaxAttempt, x.Score, x.OrderIndex, x.IsActive,
                stat?.Count ?? 0, stat?.ForcedPassCount ?? 0,
                x.OptionList.OrderBy(o => o.OrderIndex)
                    .Select(o => new VideoQuestionOptionManageDto(o.Id, o.Content, o.IsCorrect, o.Feedback, o.OrderIndex))
                    .ToList());
        }).ToList();

        return Result.Success(list);
    }

    public async Task<Result> Create(int lessonId, VideoQuestionRequest request)
    {
        var lesson = await _db.Lesson.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lessonId);
        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));
        if (lesson.ContentType != LessonContentType.Video)
            return Result.Invalid(new MessageResponse("Chỉ bài học dạng Video mới chèn được câu hỏi vào giữa video."));

        var error = ValidateOption(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        if (await _db.VideoQuestion.AnyAsync(x => x.LessonId == lessonId && x.AtSecond == request.AtSecond))
            return Result.Invalid(new MessageResponse($"Mốc {FormatSecond(request.AtSecond)} đã có câu hỏi khác."));

        var maxOrder = await _db.VideoQuestion.Where(x => x.LessonId == lessonId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        var question = new VideoQuestion
        {
            LessonId = lessonId,
            AtSecond = request.AtSecond,
            Content = request.Content.Trim(),
            QuestionType = request.QuestionType,
            Hint = string.IsNullOrWhiteSpace(request.Hint) ? null : request.Hint.Trim(),
            MustAnswerCorrectly = request.MustAnswerCorrectly,
            BlockSeek = request.BlockSeek,
            MaxAttempt = NormalizeMaxAttempt(request),
            Score = request.Score,
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            IsActive = request.IsActive
        };

        FillOptionList(question, request);

        _db.VideoQuestion.Add(question);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse($"Đã thêm câu hỏi tại mốc {FormatSecond(question.AtSecond)}."));
    }

    public async Task<Result> Update(int id, VideoQuestionRequest request)
    {
        var question = await _db.VideoQuestion.Include(x => x.OptionList).FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu hỏi."));

        var error = ValidateOption(request);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        if (await _db.VideoQuestion.AnyAsync(x => x.LessonId == question.LessonId
                                                  && x.AtSecond == request.AtSecond && x.Id != id))
            return Result.Invalid(new MessageResponse($"Mốc {FormatSecond(request.AtSecond)} đã có câu hỏi khác."));

        question.AtSecond = request.AtSecond;
        question.Content = request.Content.Trim();
        question.QuestionType = request.QuestionType;
        question.Hint = string.IsNullOrWhiteSpace(request.Hint) ? null : request.Hint.Trim();
        question.MustAnswerCorrectly = request.MustAnswerCorrectly;
        question.BlockSeek = request.BlockSeek;
        question.MaxAttempt = NormalizeMaxAttempt(request);
        question.Score = request.Score;
        question.OrderIndex = request.OrderIndex;
        question.IsActive = request.IsActive;

        // Sửa lựa chọn = xóa hết rồi thêm lại (giống UpdateNode của mô phỏng 3D). Bản ghi trả lời cũ vẫn giữ
        // Id lựa chọn đã chọn nên chỉ còn giá trị lịch sử — chấp nhận được vì nội dung câu hỏi đã đổi.
        _db.VideoQuestionOption.RemoveRange(question.OptionList);
        FillOptionList(question, request);

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật câu hỏi thành công."));
    }

    public async Task<Result> Delete(int id)
    {
        var question = await _db.VideoQuestion.FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu hỏi."));

        // Đã có học viên trả lời -> ẩn thay vì xóa để giữ lịch sử học tập
        if (await _db.VideoQuestionAnswer.AnyAsync(x => x.VideoQuestionId == id))
        {
            question.IsActive = false;
            await _db.SaveChangesAsync();
            return Result.Success(new MessageResponse("Câu hỏi đã có học viên trả lời nên được ẩn thay vì xóa."));
        }

        _db.VideoQuestion.Remove(question);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa câu hỏi."));
    }

    /// <summary>Giới hạn số lần trả lời chỉ có ý nghĩa với câu BẮT BUỘC ĐÚNG — câu thường thì trả lời một
    /// lần là video chạy tiếp, có đặt giới hạn cũng không bao giờ chạm tới. Ép về 0 để dữ liệu không mang
    /// một con số vô nghĩa mà giao diện lại đem ra hiển thị ("còn 2 lượt" cho câu chỉ trả lời 1 lần).</summary>
    private static int NormalizeMaxAttempt(VideoQuestionRequest request)
        => request.MustAnswerCorrectly ? Math.Max(0, request.MaxAttempt) : 0;

    private static void FillOptionList(VideoQuestion question, VideoQuestionRequest request)
    {
        var order = 1;
        foreach (var option in request.OptionList)
        {
            question.OptionList.Add(new VideoQuestionOption
            {
                Content = option.Content.Trim(),
                IsCorrect = option.IsCorrect,
                Feedback = string.IsNullOrWhiteSpace(option.Feedback) ? null : option.Feedback.Trim(),
                OrderIndex = option.OrderIndex > 0 ? option.OrderIndex : order
            });
            order++;
        }
    }

    /// <summary>Câu hỏi phải có ít nhất 2 lựa chọn; loại chọn một đáp án / đúng-sai phải có đúng 1 đáp án đúng,
    /// loại chọn nhiều đáp án phải có ít nhất 1 — nếu không việc chấm đúng/sai là vô nghĩa.</summary>
    private static string? ValidateOption(VideoQuestionRequest request)
    {
        var optionList = request.OptionList.Where(x => !string.IsNullOrWhiteSpace(x.Content)).ToList();
        if (optionList.Count < 2) return "Câu hỏi phải có ít nhất 2 lựa chọn.";

        var correctCount = optionList.Count(x => x.IsCorrect);
        if (request.QuestionType == QuestionType.MultipleChoice)
        {
            if (correctCount < 1) return "Câu hỏi chọn nhiều đáp án phải có ít nhất một đáp án đúng.";
        }
        else if (correctCount != 1)
        {
            return "Câu hỏi phải có đúng một đáp án đúng.";
        }

        request.OptionList = optionList;
        return null;
    }

    public async Task<Result> GetChapterForManage(int lessonId)
    {
        var list = await _db.VideoChapter.AsNoTracking()
            .Where(x => x.LessonId == lessonId)
            .OrderBy(x => x.StartSecond).ThenBy(x => x.OrderIndex)
            .Select(x => new VideoChapterDto(x.Id, x.StartSecond, x.EndSecond, x.Title, x.Topic,
                x.OrderIndex, x.IsActive))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> CreateChapter(int lessonId, VideoChapterRequest request)
    {
        var lesson = await _db.Lesson.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lessonId);
        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));
        if (lesson.ContentType != LessonContentType.Video)
            return Result.Invalid(new MessageResponse("Chỉ bài học dạng Video mới chia được chương mục."));

        var error = await ValidateChapterAsync(lessonId, request, 0);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var maxOrder = await _db.VideoChapter.Where(x => x.LessonId == lessonId)
            .Select(x => (int?)x.OrderIndex).MaxAsync() ?? 0;

        _db.VideoChapter.Add(new VideoChapter
        {
            LessonId = lessonId,
            StartSecond = request.StartSecond,
            EndSecond = request.EndSecond,
            Title = request.Title.Trim(),
            Topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim(),
            OrderIndex = request.OrderIndex > 0 ? request.OrderIndex : maxOrder + 1,
            IsActive = request.IsActive
        });
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse(
            $"Đã thêm chương {FormatSecond(request.StartSecond)}–{FormatSecond(request.EndSecond)}."));
    }

    public async Task<Result> UpdateChapter(int id, VideoChapterRequest request)
    {
        var chapter = await _db.VideoChapter.FirstOrDefaultAsync(x => x.Id == id);
        if (chapter is null) return Result.NotFound(new MessageResponse("Không tìm thấy chương mục."));

        var error = await ValidateChapterAsync(chapter.LessonId, request, id);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        chapter.StartSecond = request.StartSecond;
        chapter.EndSecond = request.EndSecond;
        chapter.Title = request.Title.Trim();
        chapter.Topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        chapter.OrderIndex = request.OrderIndex;
        chapter.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật chương mục thành công."));
    }

    public async Task<Result> DeleteChapter(int id)
    {
        var chapter = await _db.VideoChapter.FirstOrDefaultAsync(x => x.Id == id);
        if (chapter is null) return Result.NotFound(new MessageResponse("Không tìm thấy chương mục."));

        // Xóa thẳng, không ẩn như câu hỏi: chương mục chỉ là nhãn chỉ đường, không có bản ghi kết quả nào
        // của học viên trỏ vào nên xóa đi không mất lịch sử học tập của ai.
        _db.VideoChapter.Remove(chapter);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa chương mục."));
    }

    /// <summary>Kiểm tra một đoạn chương trước khi lưu. Trả về null nếu hợp lệ, ngược lại là câu báo lỗi.
    ///
    /// Chỗ đáng nói là kiểm CHỒNG LẤN: hai đoạn phủ lên nhau thì tại một giây bất kỳ trong phần giao nhau,
    /// trình phát có hai chương cùng đúng và buộc phải chọn bừa một cái — học viên xem hai lần cùng một đoạn
    /// lại thấy hai tên chương khác nhau. CSDL không diễn tả được ràng buộc "không giao nhau" (index duy
    /// nhất chỉ chặn được trùng khít mốc bắt đầu) nên phải chặn ở đây.
    ///
    /// Hai đoạn liền kề dùng chung một mốc (đoạn A kết thúc giây 60, đoạn B bắt đầu giây 60) KHÔNG tính là
    /// chồng lấn — đó là cách chia liền mạch thông thường, và trình phát coi mốc kết thúc là mốc mở nên
    /// giây 60 chỉ thuộc về đoạn B.</summary>
    private async Task<string?> ValidateChapterAsync(int lessonId, VideoChapterRequest request, int selfId)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Chương mục phải có tên.";
        if (request.EndSecond <= request.StartSecond)
            return "Mốc kết thúc phải lớn hơn mốc bắt đầu.";

        var overlap = await _db.VideoChapter.AsNoTracking()
            .Where(x => x.LessonId == lessonId && x.Id != selfId)
            .Where(x => x.StartSecond < request.EndSecond && request.StartSecond < x.EndSecond)
            .OrderBy(x => x.StartSecond)
            .FirstOrDefaultAsync();

        if (overlap is not null)
            return $"Đoạn này chồng lên chương \"{overlap.Title}\" "
                 + $"({FormatSecond(overlap.StartSecond)}–{FormatSecond(overlap.EndSecond)}).";

        return null;
    }

    public async Task<Result> GetForLearner(int lessonId, int enrollmentId = 0)
    {
        var lesson = await _db.Lesson.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lessonId);
        if (lesson is null) return Result.NotFound(new MessageResponse("Không tìm thấy bài học."));

        var lastPositionSecond = 0;
        var maxWatchedSecond = 0;
        var answerMap = new Dictionary<int, VideoQuestionAnswer>();

        if (enrollmentId > 0)
        {
            var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
            if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
            if (!_currentUser.IsManager && enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
            if (enrollment.CourseId != lesson.CourseId)
                return Result.Invalid(new MessageResponse("Bài học không thuộc khóa học này."));

            var progress = await _db.LessonProgress.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollmentId && x.LessonId == lessonId)
                .Select(x => new { x.LastPositionSecond, x.MaxWatchedSecond })
                .FirstOrDefaultAsync();
            lastPositionSecond = progress?.LastPositionSecond ?? 0;
            maxWatchedSecond = progress?.MaxWatchedSecond ?? 0;

            answerMap = await _db.VideoQuestionAnswer.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollmentId && x.VideoQuestion.LessonId == lessonId)
                .ToDictionaryAsync(x => x.VideoQuestionId);
        }
        else if (!_currentUser.IsManager)
        {
            return Result.Invalid(new MessageResponse("Thiếu mã phân công khóa học."));
        }

        var questionList = await _db.VideoQuestion.AsNoTracking()
            .Include(x => x.OptionList)
            .Where(x => x.LessonId == lessonId && x.IsActive)
            .OrderBy(x => x.AtSecond).ThenBy(x => x.OrderIndex)
            .ToListAsync();

        var checkpointList = questionList.Select(x =>
        {
            answerMap.TryGetValue(x.Id, out var answer);
            // "Đã vượt qua" gồm cả trường hợp bị cho qua vì hết lượt trả lời — nếu không tính, học viên mở
            // lại bài học sẽ bị chính câu đã hết lượt đó chặn lại lần nữa, không đi tiếp được nữa.
            var isPassed = answer is not null
                && (!x.MustAnswerCorrectly || answer.IsCorrect || answer.IsForcedPass);

            return new VideoCheckpointDto(x.Id, x.AtSecond, x.Content, x.QuestionType.ToString(), x.Hint,
                x.MustAnswerCorrectly, x.BlockSeek, x.MaxAttempt, x.Score,
                answer is not null, isPassed, answer?.IsForcedPass ?? false, answer?.EarnedScore ?? 0m,
                answer?.TryCount ?? 0,
                x.OptionList.OrderBy(o => o.OrderIndex)
                    .Select(o => new VideoOptionDto(o.Id, o.Content)).ToList());
        }).ToList();

        // Bản đồ nội dung theo thời gian. Chỉ lấy chương đang bật: chương bị ẩn là chương người soạn đang
        // sửa dở hoặc đã bỏ, hiện ra thì học viên đọc phải một cái tên không còn đúng với đoạn video đó.
        var chapterList = await _db.VideoChapter.AsNoTracking()
            .Where(x => x.LessonId == lessonId && x.IsActive)
            .OrderBy(x => x.StartSecond).ThenBy(x => x.OrderIndex)
            .Select(x => new VideoChapterDto(x.Id, x.StartSecond, x.EndSecond, x.Title, x.Topic,
                x.OrderIndex, x.IsActive))
            .ToListAsync();

        return Result.Success(new VideoLearningDto(lessonId, lastPositionSecond,
            checkpointList.Sum(x => x.EarnedScore), questionList.Sum(x => x.Score),
            lesson.VideoSeekPolicy.ToString(), maxWatchedSecond, checkpointList, chapterList));
    }

    public async Task<Result> Answer(VideoAnswerRequest request)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.EnrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        if (enrollment.Status == EnrollmentStatus.Cancelled)
            return Result.Invalid(new MessageResponse("Khóa học này đã bị hủy phân công."));

        var question = await _db.VideoQuestion.AsNoTracking()
            .Include(x => x.OptionList)
            .Include(x => x.Lesson)
            .FirstOrDefaultAsync(x => x.Id == request.VideoQuestionId && x.IsActive);

        if (question is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu hỏi."));
        if (question.Lesson.CourseId != enrollment.CourseId)
            return Result.Invalid(new MessageResponse("Câu hỏi không thuộc khóa học này."));

        // Chỉ nhận Id lựa chọn có thật trong câu hỏi này — chặn việc gửi Id tùy ý từ DevTools
        var selectedIdList = request.SelectedOptionIdList.Distinct()
            .Where(id => question.OptionList.Any(o => o.Id == id))
            .OrderBy(x => x).ToList();

        if (selectedIdList.Count == 0)
            return Result.Invalid(new MessageResponse("Bạn chưa chọn đáp án nào."));

        var correctIdList = question.OptionList.Where(o => o.IsCorrect).Select(o => o.Id).OrderBy(x => x).ToList();
        var isCorrect = selectedIdList.SequenceEqual(correctIdList);

        var answer = await _db.VideoQuestionAnswer
            .FirstOrDefaultAsync(x => x.EnrollmentId == request.EnrollmentId && x.VideoQuestionId == question.Id);

        if (answer is null)
        {
            answer = new VideoQuestionAnswer
            {
                EnrollmentId = request.EnrollmentId,
                VideoQuestionId = question.Id,
                TryCount = 1
            };
            _db.VideoQuestionAnswer.Add(answer);
        }
        else
        {
            answer.TryCount++;
        }

        // Câu bắt buộc đúng mà trả lời sai: chỉ được đi tiếp khi đã dùng hết số lượt người soạn bài cho phép
        // (MaxAttempt = 0 nghĩa là không giới hạn — phải làm tới khi đúng). Hết lượt thì video chạy tiếp
        // nhưng câu này bị trừ trọn điểm, đây là điểm khác biệt duy nhất so với việc trả lời đúng.
        var isBlocked = question.MustAnswerCorrectly && !isCorrect;
        var isForcedPass = isBlocked && question.MaxAttempt > 0 && answer.TryCount >= question.MaxAttempt;
        var canContinue = !isBlocked || isForcedPass;

        answer.SelectedOptionIdList = string.Join(",", selectedIdList);
        answer.IsCorrect = isCorrect;
        answer.EarnedScore = isCorrect ? question.Score : 0m;
        // Cờ này chỉ được bật, không bao giờ tắt lại: học viên đã bị cho qua một lần thì lần mở bài sau
        // không bị chính câu đó chặn lại nữa, kể cả khi người soạn bài nới thêm lượt về sau.
        if (isForcedPass) answer.IsForcedPass = true;
        answer.AnsweredAt = DateTime.Now;

        await _db.SaveChangesAsync();

        // Điểm xếp hạng cho câu trả lời ĐÚNG. Nguồn là chính câu hỏi, nên làm lại đúng lần nữa không cộng
        // thêm — và người trả lời sai rồi mới đúng vẫn được cộng, đó là học chứ không phải gian lận.
        //
        // Nuốt lỗi có chủ đích: đây là phần thưởng phụ, hỏng nó không được phép làm hỏng việc chấm bài —
        // học viên sẽ thấy câu trả lời của mình biến mất và phải làm lại.
        if (isCorrect)
        {
            try
            {
                await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.VideoQuestionPassed,
                    "VideoQuestion", question.Id);
            }
            catch { /* bỏ qua: không để phần thưởng làm hỏng việc chấm câu hỏi */ }
        }

        var remainingAttempt = question.MaxAttempt > 0 && question.MustAnswerCorrectly
            ? Math.Max(0, question.MaxAttempt - answer.TryCount)
            : -1;

        var feedback = string.Join(" ", question.OptionList
            .Where(o => selectedIdList.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.Feedback))
            .Select(o => o.Feedback));

        var message = isCorrect
            ? "Chính xác! Video sẽ chạy tiếp."
            : isForcedPass
                ? $"Bạn đã dùng hết {question.MaxAttempt} lượt trả lời. Video chạy tiếp nhưng câu này bị trừ trọn {question.Score:0.##} điểm."
                : canContinue
                    ? "Chưa chính xác, nhưng bạn có thể xem tiếp."
                    : remainingAttempt > 0
                        ? $"Chưa chính xác. Bạn còn {remainingAttempt} lượt trả lời."
                        : "Chưa chính xác. Bạn cần trả lời đúng mới xem tiếp được.";

        // Tổng điểm của cả bài học video, tính lại sau khi đã lưu — để trình phát cập nhật thanh điểm mà
        // không phải gọi thêm một request nữa.
        var totalScore = await _db.VideoQuestion.AsNoTracking()
            .Where(x => x.LessonId == question.LessonId && x.IsActive)
            .SumAsync(x => (decimal?)x.Score) ?? 0m;
        var totalEarnedScore = await _db.VideoQuestionAnswer.AsNoTracking()
            .Where(x => x.EnrollmentId == request.EnrollmentId && x.VideoQuestion.LessonId == question.LessonId)
            .SumAsync(x => (decimal?)x.EarnedScore) ?? 0m;

        return Result.Success(new VideoAnswerResultDto(isCorrect, canContinue, isForcedPass, message,
            string.IsNullOrWhiteSpace(feedback) ? null : feedback,
            canContinue ? correctIdList : new List<int>(),
            answer.TryCount, remainingAttempt,
            answer.EarnedScore, question.Score, totalEarnedScore, totalScore));
    }

    /// <summary>Đổi số giây thành dạng mm:ss (hoặc h:mm:ss khi video dài hơn 1 giờ) để ghép vào thông báo.</summary>
    private static string FormatSecond(int totalSecond)
    {
        var time = TimeSpan.FromSeconds(totalSecond);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
