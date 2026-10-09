// File: Application/Services/LearningService.cs
using HbELearning.Application.Features.Scorm.CoursePackage;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Services;

public class LearningService : ILearningService
{
    private readonly IAppDbContext _db;
    private readonly ICertificateIssuer _certificateService;
    private readonly IRankingEngine _rankingService;

    public LearningService(IAppDbContext db, ICertificateIssuer certificateService, IRankingEngine rankingService)
    {
        _db = db;
        _certificateService = certificateService;
        _rankingService = rankingService;
    }

    public async Task<List<PathProgressDto>> BuildPathProgressAsync(int enrollmentId)
    {
        var result = new List<PathProgressDto>();

        var enrollment = await _db.Enrollment.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return result;

        // Cấu trúc khóa học (SCORM Organization) người soạn đã dựng, hoặc cấu trúc mặc định nếu chưa có — cùng một
        // nguồn với cây hoạt động của trình phát, nên thứ tự lộ trình ở đây khớp đúng thứ tự "Tiếp" khi học.
        var (path, itemList) = await DefaultOrganization.LoadAsync(_db, enrollment.CourseId);

        var lessonDict = await _db.Lesson.AsNoTracking()
            .Where(l => l.CourseId == enrollment.CourseId && l.IsActive)
            .ToDictionaryAsync(l => l.Id);

        var quizDict = await _db.Quiz.AsNoTracking()
            .Where(q => q.CourseId == enrollment.CourseId && q.IsActive)
            .ToDictionaryAsync(q => q.Id);

        // Mô phỏng chưa có tình huống nào không giao cho học viên (không bao giờ đạt được) — trình phát cũng bỏ nó
        // khỏi cây hoạt động.
        var scenarioDict = await _db.ScenarioScene.AsNoTracking()
            .Where(s => s.CourseId == enrollment.CourseId && s.IsActive && s.NodeList.Count > 0)
            .ToDictionaryAsync(s => s.Id);

        var scormPackageDict = await _db.ScormPackage.AsNoTracking()
            .Where(p => p.CourseId == enrollment.CourseId && p.Kind == ScormPackageKind.Imported)
            .ToDictionaryAsync(p => p.Id);

        // Số câu hỏi chèn giữa video của từng bài học — gom một lượt cho cả khóa. Cây nội dung bên phải màn
        // hình học chỉ cần CON SỐ để hiện "3 câu hỏi"; nội dung từng câu do người học bấm mở mới tải
        // (VideoLearningService.GetForLearner), nên ở đây không kéo theo cả bảng câu hỏi.
        var videoQuestionCountDict = await _db.VideoQuestion.AsNoTracking()
            .Where(q => q.IsActive && lessonDict.Keys.Contains(q.LessonId))
            .GroupBy(q => q.LessonId)
            .Select(g => new { LessonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LessonId, x => x.Count);

        // Số chương của từng bài video — gom một lượt, cùng lý do với số câu hỏi ở trên. Cây nội dung cần con
        // số này để biết nhánh video có gì để bung ra: bài chỉ có chương mà chưa có câu hỏi nào vẫn phải mở
        // được mục lục.
        var videoChapterCountDict = await _db.VideoChapter.AsNoTracking()
            .Where(c => c.IsActive && lessonDict.Keys.Contains(c.LessonId))
            .GroupBy(c => c.LessonId)
            .Select(g => new { LessonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LessonId, x => x.Count);

        // Danh sách bước = các mục LÁ của cấu trúc theo đúng thứ tự cây (chương chỉ là cụm, không phải một bước học).
        var stepList = OrganizationOrder.FlattenLeaves(itemList)
            .Select((item, index) => (PathItemId: item.Id > 0 ? item.Id : 0, Type: item.ItemType, item.LessonId, item.QuizId,
                item.ScenarioSceneId, Order: index + 1, Required: item.IsRequired, item.StartDate, item.EndDate,
                item.ScormPackageId, CustomTitle: item.Title))
            .ToList();

        var progressDict = await _db.LessonProgress.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId)
            .ToDictionaryAsync(x => x.LessonId);

        // Lấy toàn bộ lượt làm bài (kể cả chưa nộp) để đếm đúng số lượt đã dùng TRONG NGÀY hôm nay —
        // giới hạn lượt làm bài (MaxAttempt) được làm mới (reset) mỗi ngày, khớp với QuizService.Start.
        var attemptList = await _db.QuizAttempt.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId)
            .ToListAsync();

        var scenarioAttemptList = await _db.ScenarioAttempt.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId)
            .ToListAsync();

        // Gói nhập ngoài được học BÊN TRONG lượt học khóa học (gói SCORM của khóa học, cụm "P{id}") — kết quả đọc thẳng
        // từ trạng thái đã lưu. Bản ghi học riêng từng gói là dữ liệu từ trước khi khóa học thành một gói SCORM: vẫn
        // tính, để không ai mất tiến độ đã có.
        var registrationList = await _db.ScormRegistration.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId)
            .Select(x => new { x.ScormPackageId, x.ScormPackage.Kind, x.StateJson, x.CompletedAt, x.LastAccessedAt, x.AttemptCount, x.BestScoreScaled })
            .ToListAsync();
        var courseStateJson = registrationList.FirstOrDefault(x => x.Kind == ScormPackageKind.Course)?.StateJson;
        var legacyRegistrationDict = registrationList.Where(x => x.Kind == ScormPackageKind.Imported)
            .ToDictionary(x => x.ScormPackageId);

        var isSequential = path?.IsSequential ?? true;
        var previousDone = true;
        var completedText = ProgressStatus.Completed.ToString();
        var now = DateTime.Now;
        var todayStart = now.Date;

        foreach (var step in stepList.OrderBy(x => x.Order))
        {
            string title;
            var duration = 0;
            string status;
            decimal? bestScore = null;
            int attemptUsed = 0, maxAttempt = 0, lastPosition = 0;
            bool hasEverAttempted = false;
            string? contentType = null;
            var videoQuestionCount = 0;
            var videoChapterCount = 0;
            int? parentLessonId = null;

            if (step.Type == PathItemType.Lesson && step.LessonId is int lessonId
                && lessonDict.TryGetValue(lessonId, out var lesson))
            {
                title = lesson.Title;
                duration = lesson.DurationMinutes;
                contentType = lesson.ContentType.ToString();
                videoQuestionCountDict.TryGetValue(lessonId, out videoQuestionCount);
                videoChapterCountDict.TryGetValue(lessonId, out videoChapterCount);

                progressDict.TryGetValue(lessonId, out var progress);
                status = (progress?.Status ?? ProgressStatus.NotStarted).ToString();
                lastPosition = progress?.LastPositionSecond ?? 0;
            }
            else if (step.Type == PathItemType.Quiz && step.QuizId is int quizId
                && quizDict.TryGetValue(quizId, out var quiz))
            {
                title = quiz.Title;
                duration = quiz.TimeLimitMinutes;
                maxAttempt = quiz.MaxAttempt;
                parentLessonId = quiz.LessonId;

                var myAttemptList = attemptList.Where(a => a.QuizId == quizId).ToList();
                var mySubmittedList = myAttemptList.Where(a => a.SubmittedAt != null).ToList();

                attemptUsed = myAttemptList.Count(a => a.StartedAt >= todayStart);
                hasEverAttempted = myAttemptList.Count > 0;
                bestScore = mySubmittedList.Count > 0 ? mySubmittedList.Max(a => a.Score) : null;

                status = mySubmittedList.Any(a => a.IsPassed) ? completedText
                       : mySubmittedList.Count > 0 ? ProgressStatus.InProgress.ToString()
                       : ProgressStatus.NotStarted.ToString();
            }
            else if (step.Type == PathItemType.Simulation && step.ScenarioSceneId is int sceneId
                && scenarioDict.TryGetValue(sceneId, out var scene))
            {
                title = scene.Title;
                parentLessonId = scene.LessonId;
                duration = 0; // không có thời lượng cố định — khám phá tự do trong cảnh 3D
                maxAttempt = 0; // không giới hạn số lượt chơi/ngày, khác Quiz (điểm có thể âm nên cho chơi lại thoải mái)

                var myAttemptList = scenarioAttemptList.Where(a => a.ScenarioSceneId == sceneId).ToList();
                var myCompletedList = myAttemptList.Where(a => a.Status == ScenarioAttemptStatus.Completed).ToList();

                attemptUsed = myAttemptList.Count(a => a.StartedAt >= todayStart);
                hasEverAttempted = myAttemptList.Count > 0;
                bestScore = myCompletedList.Count > 0 ? (decimal?)myCompletedList.Max(a => a.Score) : null;

                status = myCompletedList.Any(a => a.IsPassed) ? completedText
                       : myCompletedList.Count > 0 ? ProgressStatus.InProgress.ToString()
                       : ProgressStatus.NotStarted.ToString();
            }
            else if (step.Type == PathItemType.Scorm && step.ScormPackageId is int packageId
                && scormPackageDict.TryGetValue(packageId, out var scormPackage))
            {
                title = scormPackage.Title;
                duration = scormPackage.DurationMinutes;
                var cluster = CourseScormService.PackageState(courseStateJson, packageId);
                legacyRegistrationDict.TryGetValue(packageId, out var legacy);
                hasEverAttempted = cluster is { AttemptCount: > 0 } || legacy?.LastAccessedAt is not null;
                attemptUsed = Math.Max(cluster?.AttemptCount ?? 0, legacy?.AttemptCount ?? 0);
                var bestScaled = new[] { cluster?.BestMeasure is double m ? (decimal?)m : null, legacy?.BestScoreScaled }.Max();
                bestScore = bestScaled is decimal best ? Math.Round(Math.Clamp(best, -1m, 1m) * 100, 2) : null;

                // Hoàn thành do CHÍNH GÓI quyết định (rollup của cụm: đạt, hoặc hoàn thành mà không bị đánh trượt) —
                // và đã đạt một lần là giữ, học lại không kéo bước về "chưa xong".
                var done = cluster is not null && (cluster.EverSatisfied || (cluster.EverCompleted && !cluster.EverFailed));
                status = done || legacy?.CompletedAt is not null ? completedText
                       : hasEverAttempted ? ProgressStatus.InProgress.ToString()
                       : ProgressStatus.NotStarted.ToString();
            }
            else
            {
                continue; // bước trỏ tới bài học/bài kiểm tra/mô phỏng/gói SCORM đã bị vô hiệu hóa
            }

            // Bước có gán lịch (StartDate/EndDate) -> ngày tháng là điều kiện mở/khóa duy nhất, bỏ qua tuần tự.
            // Bước không gán lịch -> giữ nguyên hành vi cũ, chỉ xét tuần tự.
            var hasSchedule = step.StartDate is not null || step.EndDate is not null;
            bool isUnlocked;
            if (hasSchedule)
            {
                isUnlocked = step.Type is PathItemType.Quiz or PathItemType.Simulation
                    ? (step.StartDate is null || now >= step.StartDate) && (step.EndDate is null || now <= step.EndDate)
                    : step.StartDate is null || now >= step.StartDate; // Bài học: chỉ chặn trước StartDate, không khóa lại sau EndDate
            }
            else
            {
                isUnlocked = !isSequential || previousDone;
            }

            // Tên người soạn đặt riêng cho mục (nếu có) — đúng chữ học viên thấy trong mục lục của trình phát.
            if (!string.IsNullOrWhiteSpace(step.CustomTitle)) title = step.CustomTitle.Trim();

            result.Add(new PathProgressDto(
                step.PathItemId, step.Type.ToString(), step.LessonId, step.QuizId,
                title, duration, step.Order, step.Required,
                status, isUnlocked, bestScore, attemptUsed, maxAttempt, lastPosition,
                step.StartDate, step.EndDate, hasEverAttempted, step.ScenarioSceneId,
                contentType, videoQuestionCount, parentLessonId, videoChapterCount, step.ScormPackageId));

            // Lộ trình tuần tự: gặp bước bắt buộc chưa xong thì khóa toàn bộ bước sau
            if (step.Required && status != completedText) previousDone = false;
        }

        return result;
    }

    public async Task RecalculateAsync(int enrollmentId)
    {
        var enrollment = await _db.Enrollment
            .Include(x => x.Course)
            .FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return;

        var pathList = await BuildPathProgressAsync(enrollmentId);
        var completedText = ProgressStatus.Completed.ToString();

        var requiredList = pathList.Where(x => x.IsRequired).ToList();
        var completedCount = requiredList.Count(x => x.Status == completedText);

        // Điểm cuối khóa = điểm cao nhất trong các bài kiểm tra đánh dấu IsFinalExam
        var finalQuizIdList = await _db.Quiz
            .Where(q => q.CourseId == enrollment.CourseId && q.IsFinalExam && q.IsActive)
            .Select(q => q.Id)
            .ToListAsync();

        decimal? bestFinalScore = null;
        if (finalQuizIdList.Count > 0)
        {
            var scoreList = await _db.QuizAttempt
                .Where(a => a.EnrollmentId == enrollmentId
                         && finalQuizIdList.Contains(a.QuizId)
                         && a.SubmittedAt != null)
                .Select(a => a.Score)
                .ToListAsync();

            if (scoreList.Count > 0) bestFinalScore = scoreList.Max();
        }
        else
        {
            // Không có bài thi cuối khóa nhưng có gói SCORM tự chấm điểm (khóa học nhập nguyên từ gói SCORM là
            // trường hợp điển hình): điểm cuối khóa — điểm in trên chứng nhận — lấy trung bình điểm cao nhất của
            // các gói. Để trống thì chứng nhận ghi 100 điểm cho người gói chấm 60.
            var scormScoreList = pathList.Where(x => x.ScormPackageId.HasValue && x.BestScore.HasValue)
                .Select(x => x.BestScore!.Value).ToList();
            if (scormScoreList.Count > 0)
                bestFinalScore = Math.Round(Math.Clamp(scormScoreList.Average(), 0m, 100m), 2);
        }
        // Quy tắc tính % và quyết định hoàn thành nằm ở thực thể — xem Enrollment.ApplyProgress.
        enrollment.ApplyProgress(requiredList.Count, completedCount, finalQuizIdList.Count > 0,
            bestFinalScore, enrollment.Course.PassScore, DateTime.Now);

        await _db.SaveChangesAsync();

        // Đủ điều kiện -> cấp chứng nhận (idempotent, gọi nhiều lần không tạo trùng)
        if (enrollment.Status == EnrollmentStatus.Completed)
            await _certificateService.IssueAsync(enrollmentId, bestFinalScore ?? 100m);

        // Cộng điểm xếp hạng (Ranking) — idempotent theo (UserId, Reason, SourceType, SourceId) nên gọi lại
        // mỗi lần tính toán tiến độ không bị cộng trùng, tự "bắt kịp" các bước vừa hoàn thành.
        foreach (var step in pathList.Where(x => x.Status == completedText))
        {
            if (step.ItemType == nameof(PathItemType.Lesson) && step.LessonId is int lessonId)
                await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.LessonCompleted, "Lesson", lessonId);
            else if (step.ItemType == nameof(PathItemType.Quiz) && step.QuizId is int quizId)
                await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.QuizPassed, "Quiz", quizId);
            else if (step.ItemType == nameof(PathItemType.Simulation) && step.ScenarioSceneId is int sceneId)
                await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.SimulationCompleted, "Scenario", sceneId);
            // Một gói SCORM là cả một bài học trọn gói — cộng như hoàn thành bài học, nguồn riêng "Scorm".
            else if (step.ItemType == nameof(PathItemType.Scorm) && step.ScormPackageId is int packageId)
                await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.LessonCompleted, "Scorm", packageId);
        }
        if (enrollment.Status == EnrollmentStatus.Completed)
            await _rankingService.AwardPointsAsync(enrollment.UserId, PointReason.CourseCompleted, "Course", enrollment.CourseId);
    }
}
