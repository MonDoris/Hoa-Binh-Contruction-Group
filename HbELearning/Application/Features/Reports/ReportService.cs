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
        return Result.Success(new PublicStatDto(
            await _db.Course.CountAsync(x => x.Status == CourseStatus.Published),
            await _db.Lesson.CountAsync(x => x.IsActive),
            await _db.Quiz.CountAsync(x => x.IsActive),
            await _db.Enrollment.CountAsync(x => x.Status == EnrollmentStatus.Completed),
            await _db.Certificate.CountAsync(x => !x.IsRevoked),
            await _db.User.CountAsync(x => x.IsActive)));
    }

    public async Task<Result> GetOverview()
    {
        var now = DateTime.Now;

        var totalUser = await _db.User.CountAsync();
        var activeUser = await _db.User.CountAsync(x => x.IsActive);
        var totalCourse = await _db.Course.CountAsync();
        var publishedCourse = await _db.Course.CountAsync(x => x.Status == CourseStatus.Published);

        var totalEnrollment = await _db.Enrollment.CountAsync(x => x.Status != EnrollmentStatus.Cancelled);
        var completed = await _db.Enrollment.CountAsync(x => x.Status == EnrollmentStatus.Completed);
        var inProgress = await _db.Enrollment.CountAsync(x => x.Status == EnrollmentStatus.InProgress);
        var notStarted = await _db.Enrollment.CountAsync(x => x.Status == EnrollmentStatus.Assigned);
        var overdue = await _db.Enrollment.CountAsync(x =>
            x.DueDate != null && x.DueDate < now && x.Status != EnrollmentStatus.Completed
            && x.Status != EnrollmentStatus.Cancelled);

        var totalCertificate = await _db.Certificate.CountAsync(x => !x.IsRevoked);

        var averageProgress = totalEnrollment == 0 ? 0m
            : await _db.Enrollment.Where(x => x.Status != EnrollmentStatus.Cancelled)
                .AverageAsync(x => x.ProgressPercent);

        var completionRate = totalEnrollment == 0 ? 0m
            : Math.Round(completed * 100m / totalEnrollment, 2);

        return Result.Success(new OverviewReportDto(totalUser, activeUser, totalCourse, publishedCourse,
            totalEnrollment, completed, inProgress, notStarted, overdue, totalCertificate,
            completionRate, Math.Round(averageProgress, 2)));
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

        var list = raw.Select(x => new CourseReportDto(
            x.Id, x.Code, x.Title, x.Status.ToString(),
            x.AssignedCount, x.CompletedCount, x.InProgressCount, x.OverdueCount,
            x.AssignedCount == 0 ? 0m : Math.Round(x.CompletedCount * 100m / x.AssignedCount, 2),
            x.AssignedCount == 0 ? 0m : Math.Round(x.SumProgress / x.AssignedCount, 2),
            x.AverageScore.HasValue ? Math.Round(x.AverageScore.Value, 2) : null,
            x.CertificateCount))
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
