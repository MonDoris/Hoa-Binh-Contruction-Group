// File: Application/Features/Reports/ReportService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Reports;

public class ReportService : IReportService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ReportService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetPublicStat()
    {
        // Trang chủ công khai chỉ khoe nội dung ĐANG HIỆN HÀNH: bài học / bài kiểm tra của khóa còn là bản nháp, chờ
        // duyệt, bị từ chối hay đã lưu trữ chưa ai học được, đếm vào là con số trên trang chủ lệch với thứ khóa học
        // thật sự cung cấp (và lộ cả việc công ty đang soạn gì).
        return Result.Success(new PublicStatDto(
            await _db.Course.CountAsync(x => x.Status == CourseStatus.Published),
            await _db.Lesson.CountAsync(x => x.IsActive && x.Course.Status == CourseStatus.Published),
            await _db.Quiz.CountAsync(x => x.IsActive && x.Course.Status == CourseStatus.Published),
            await _db.Enrollment.CountAsync(x => x.Status == EnrollmentStatus.Completed),
            await _db.Certificate.CountAsync(x => !x.IsRevoked),
            await _db.User.CountAsync(x => x.IsActive)));
    }

    public async Task<Result> GetOverview()
    {
        var now = DateTime.Now;

        // Giảng Viên là người được mời tới dạy chứ không phải nhân sự công ty: chỉ được thấy số liệu của các khóa
        // CHÍNH họ tạo. Quy mô nhân sự và hàng chờ trao quà là thông tin nội bộ nên không trả về —
        // cắt ở đây chứ không chỉ giấu ô trên giao diện, vì API này gọi thẳng được.
        var ownOnly = _currentUser.IsInstructor;
        var ownerId = _currentUser.UserId;

        var courseQuery = _db.Course.AsQueryable();
        var enrollmentQuery = _db.Enrollment.AsQueryable();
        var certificateQuery = _db.Certificate.Where(x => !x.IsRevoked);
        if (ownOnly)
        {
            courseQuery = courseQuery.Where(x => x.CreatedById == ownerId);
            enrollmentQuery = enrollmentQuery.Where(x => x.Course.CreatedById == ownerId);
        }

        // Tổng chứng chỉ toàn công ty chỉ Admin được xem; mọi vai trò khác (kể cả Giảng Viên) chỉ đếm chứng chỉ
        // CỦA CHÍNH MÌNH. Cắt ở server để gọi thẳng API cũng không lấy được con số chung.
        if (!_currentUser.IsAdmin)
            certificateQuery = certificateQuery.Where(x => x.Enrollment.UserId == ownerId);

        var totalUser = ownOnly ? 0 : await _db.User.CountAsync();
        var activeUser = ownOnly ? 0 : await _db.User.CountAsync(x => x.IsActive);
        var totalCourse = await courseQuery.CountAsync();
        var publishedCourse = await courseQuery.CountAsync(x => x.Status == CourseStatus.Published);

        var totalEnrollment = await enrollmentQuery.CountAsync(x => x.Status != EnrollmentStatus.Cancelled);
        var completed = await enrollmentQuery.CountAsync(x => x.Status == EnrollmentStatus.Completed);
        var inProgress = await enrollmentQuery.CountAsync(x => x.Status == EnrollmentStatus.InProgress);
        var notStarted = await enrollmentQuery.CountAsync(x => x.Status == EnrollmentStatus.Assigned);
        var overdue = await enrollmentQuery.CountAsync(x =>
            x.DueDate != null && x.DueDate < now && x.Status != EnrollmentStatus.Completed
            && x.Status != EnrollmentStatus.Cancelled);

        // Số người học thật sự (đếm người, không đếm lượt) trong phạm vi đang xem — với Giảng Viên là học viên của
        // các khóa họ dạy.
        var learnerCount = await enrollmentQuery.Where(x => x.Status != EnrollmentStatus.Cancelled)
            .Select(x => x.UserId).Distinct().CountAsync();

        var totalCertificate = await certificateQuery.CountAsync();
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var certificateThisMonth = await certificateQuery.CountAsync(x => x.IssuedAt >= monthStart);

        // Quà chờ trao đếm theo LƯỢT ĐỔI chứ không cộng Quantity, cho khớp con số "Chờ trao N lượt" ở trang Cửa hàng.
        var pendingGiftQuery = _db.ShopRedemption.Where(x => x.Status == ShopRedemptionStatus.Pending);
        var pendingGift = ownOnly ? 0 : await pendingGiftQuery.CountAsync();
        var pendingGiftUser = ownOnly ? 0 : await pendingGiftQuery.Select(x => x.UserId).Distinct().CountAsync();

        // Sao trung bình của MỌI đánh giá trên các khóa học do CHÍNH người xem tạo (gộp chung, không trung bình
        // theo từng khóa: khóa nhiều đánh giá phải có sức nặng hơn khóa chỉ có một). Người chưa soạn khóa nào, hoặc
        // khóa chưa ai đánh giá, thì không có điểm — trả null để giao diện hiện "chưa có" thay vì số 0 sai nghĩa.
        var myRatingList = await _db.CourseFeedback.AsNoTracking()
            .Where(x => x.Kind == FeedbackKind.Review && x.Rating != null && x.Course.CreatedById == _currentUser.UserId)
            .Select(x => x.Rating!.Value)
            .ToListAsync();
        decimal? myAverageRating = myRatingList.Count == 0 ? null : Math.Round((decimal)myRatingList.Average(), 1);

        var averageProgress = totalEnrollment == 0 ? 0m
            : await enrollmentQuery.Where(x => x.Status != EnrollmentStatus.Cancelled)
                .AverageAsync(x => x.ProgressPercent);

        var completionRate = totalEnrollment == 0 ? 0m
            : Math.Round(completed * 100m / totalEnrollment, 2);

        return Result.Success(new OverviewReportDto(totalUser, activeUser, totalCourse, publishedCourse,
            totalEnrollment, completed, inProgress, notStarted, overdue, totalCertificate,
            completionRate, Math.Round(averageProgress, 2), certificateThisMonth, pendingGift, pendingGiftUser,
            myAverageRating, myRatingList.Count, learnerCount));
    }

    public async Task<Result> GetCompletionTrend(int? year)
    {
        var currentYear = DateTime.Now.Year;

        // Xếp lượt đào tạo theo THÁNG ĐƯỢC PHÂN CÔNG: mỗi lượt đứng đúng một cột và không nhảy cột khi người học
        // hoàn thành muộn. Xếp theo ngày hoàn thành thì lượt chưa xong không có cột nào để đứng. Lượt đã hủy không
        // tính — người phụ trách đã gỡ thì đó không còn là việc "chưa hoàn thành" của ai cả.
        var rowList = await _db.Enrollment.AsNoTracking()
            .Where(x => x.Status != EnrollmentStatus.Cancelled)
            .GroupBy(x => new { x.AssignedAt.Year, x.AssignedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Completed = g.Count(x => x.Status == EnrollmentStatus.Completed),
                Total = g.Count()
            })
            .ToListAsync();

        // Luôn có ít nhất 5 năm gần nhất: một cột đơn độc không cho thấy xu hướng nào, còn năm trống hiện cột 0
        // mới là câu trả lời đúng ("năm đó chưa ai được giao khóa nào").
        var firstYear = Math.Min(rowList.Count > 0 ? rowList.Min(x => x.Year) : currentYear, currentYear - 4);
        var lastYear = Math.Max(rowList.Count > 0 ? rowList.Max(x => x.Year) : currentYear, currentYear);
        var selectedYear = year is { } y && y >= firstYear && y <= lastYear ? y : currentYear;

        var monthList = Enumerable.Range(1, 12)
            .Select(month =>
            {
                var row = rowList.FirstOrDefault(x => x.Year == selectedYear && x.Month == month);
                return row is null
                    ? new CompletionTrendPointDto(month, 0, 0)
                    : new CompletionTrendPointDto(month, row.Completed, row.Total - row.Completed);
            })
            .ToList();

        var yearList = Enumerable.Range(firstYear, lastYear - firstYear + 1)
            .Select(y => new CompletionTrendPointDto(y,
                rowList.Where(x => x.Year == y).Sum(x => x.Completed),
                rowList.Where(x => x.Year == y).Sum(x => x.Total - x.Completed)))
            .ToList();

        return Result.Success(new CompletionTrendDto(selectedYear, monthList, yearList));
    }

    public async Task<Result> GetCourseReport()
    {
        var now = DateTime.Now;

        var raw = await _db.Course.AsNoTracking()
            .Select(c => new
            {
                c.Id, c.Code, c.Title, c.Status,
                AssignedCount = c.EnrollmentList.Count(e => e.Status != EnrollmentStatus.Cancelled),
                CompletedCount = c.EnrollmentList.Count(e => e.Status == EnrollmentStatus.Completed),
                InProgressCount = c.EnrollmentList.Count(e => e.Status == EnrollmentStatus.InProgress),
                OverdueCount = c.EnrollmentList.Count(e => e.DueDate != null && e.DueDate < now
                    && e.Status != EnrollmentStatus.Completed && e.Status != EnrollmentStatus.Cancelled),
                SumProgress = c.EnrollmentList.Where(e => e.Status != EnrollmentStatus.Cancelled)
                    .Sum(e => (decimal?)e.ProgressPercent) ?? 0m,
                AverageScore = c.EnrollmentList.Where(e => e.FinalScore != null)
                    .Average(e => (decimal?)e.FinalScore),
                CertificateCount = c.EnrollmentList.Count(e => e.Certificate != null && !e.Certificate.IsRevoked)
            })
            .ToListAsync();

        // Đánh giá sao gom riêng theo khóa rồi ghép trong bộ nhớ — tránh thêm navigation vào Course chỉ cho báo cáo.
        var ratingByCourse = (await _db.CourseFeedback.AsNoTracking()
                .Where(f => f.Kind == FeedbackKind.Review && f.Rating != null)
                .Select(f => new { f.CourseId, Rating = f.Rating!.Value })
                .ToListAsync())
            .GroupBy(f => f.CourseId)
            .ToDictionary(g => g.Key, g => (Average: Math.Round((decimal)g.Average(f => f.Rating), 1), Count: g.Count()));

        var list = raw.Select(x => new CourseReportDto(
            x.Id, x.Code, x.Title, x.Status.ToString(),
            x.AssignedCount, x.CompletedCount, x.InProgressCount, x.OverdueCount,
            x.AssignedCount == 0 ? 0m : Math.Round(x.CompletedCount * 100m / x.AssignedCount, 2),
            x.AssignedCount == 0 ? 0m : Math.Round(x.SumProgress / x.AssignedCount, 2),
            x.AverageScore.HasValue ? Math.Round(x.AverageScore.Value, 2) : null,
            x.CertificateCount,
            ratingByCourse.TryGetValue(x.Id, out var rating) ? rating.Average : null,
            ratingByCourse.TryGetValue(x.Id, out var rating2) ? rating2.Count : 0))
            .OrderByDescending(x => x.AssignedCount)
            .ToList();

        return Result.Success(list);
    }

    public async Task<Result> GetDepartmentReport()
    {
        var query = _db.Department.AsNoTracking().AsQueryable();
        if (_currentUser.IsDirector) query = query.Where(d => d.Id == _currentUser.DepartmentId);

        var list = await query
            .Select(d => new
            {
                d.Id, d.Code, d.Name,
                UserCount = d.UserList.Count(u => u.IsActive),
                AssignedCount = d.UserList.SelectMany(u => u.EnrollmentList)
                    .Count(e => e.Status != EnrollmentStatus.Cancelled),
                CompletedCount = d.UserList.SelectMany(u => u.EnrollmentList)
                    .Count(e => e.Status == EnrollmentStatus.Completed),
                AverageScore = d.UserList.SelectMany(u => u.EnrollmentList)
                    .Where(e => e.FinalScore != null).Average(e => (decimal?)e.FinalScore)
            })
            .ToListAsync();

        var result = list.Select(x => new DepartmentReportDto(
            x.Id, x.Code, x.Name, x.UserCount, x.AssignedCount, x.CompletedCount,
            x.AssignedCount == 0 ? 0m : Math.Round(x.CompletedCount * 100m / x.AssignedCount, 2),
            x.AverageScore.HasValue ? Math.Round(x.AverageScore.Value, 2) : null))
            .OrderBy(x => x.DepartmentCode)
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result> GetLearnerReport(int? departmentId)
    {
        var now = DateTime.Now;

        if (_currentUser.IsHeadOfDepartment || _currentUser.IsDirector) departmentId = _currentUser.DepartmentId;

        var query = _db.User.AsNoTracking().Where(x => x.IsActive);
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId);

        var raw = await query.Select(u => new
        {
            u.Id, u.EmployeeCode, u.FullName,
            DepartmentName = u.Department != null ? u.Department.Name : null,
            PositionName = u.Position != null ? u.Position.Name : null,
            AssignedCount = u.EnrollmentList.Count(e => e.Status != EnrollmentStatus.Cancelled),
            CompletedCount = u.EnrollmentList.Count(e => e.Status == EnrollmentStatus.Completed),
            OverdueCount = u.EnrollmentList.Count(e => e.DueDate != null && e.DueDate < now
                && e.Status != EnrollmentStatus.Completed && e.Status != EnrollmentStatus.Cancelled),
            SumProgress = u.EnrollmentList.Where(e => e.Status != EnrollmentStatus.Cancelled)
                .Sum(e => (decimal?)e.ProgressPercent) ?? 0m,
            AverageScore = u.EnrollmentList.Where(e => e.FinalScore != null).Average(e => (decimal?)e.FinalScore),
            CertificateCount = u.EnrollmentList.Count(e => e.Certificate != null && !e.Certificate.IsRevoked)
        }).ToListAsync();

        var list = raw.Select(x => new LearnerReportDto(
            x.Id, x.EmployeeCode, x.FullName, x.DepartmentName, x.PositionName,
            x.AssignedCount, x.CompletedCount, x.OverdueCount,
            x.AssignedCount == 0 ? 0m : Math.Round(x.SumProgress / x.AssignedCount, 2),
            x.AverageScore.HasValue ? Math.Round(x.AverageScore.Value, 2) : null,
            x.CertificateCount))
            .OrderBy(x => x.EmployeeCode)
            .ToList();

        return Result.Success(list);
    }
}
