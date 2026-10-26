// File: Application/DTOs/ReportDto.cs
namespace HbELearning.Application.DTOs;

public record OverviewReportDto(
    int TotalUser, int ActiveUser, int TotalCourse, int PublishedCourse,
    int TotalEnrollment, int CompletedEnrollment, int InProgressEnrollment,
    int NotStartedEnrollment, int OverdueEnrollment,
    int TotalCertificate, decimal CompletionRate, decimal AverageProgress,
    int CertificateThisMonth, int PendingGift, int PendingGiftUser,
    decimal? MyAverageRating, int MyReviewCount, int LearnerCount);

/// <summary>Số lượt đào tạo hoàn thành / chưa hoàn thành của một mốc thời gian.</summary>
/// <param name="Period">Tháng (1–12) ở chuỗi theo tháng, hoặc năm (VD 2026) ở chuỗi theo năm.</param>
public record CompletionTrendPointDto(int Period, int CompletedCount, int NotCompletedCount);

/// <summary>Số liệu cho hai biểu đồ cột ở trang Tổng quan: đủ 12 tháng của <paramref name="Year"/>, và từng năm.</summary>
public record CompletionTrendDto(int Year,
    IReadOnlyList<CompletionTrendPointDto> MonthList, IReadOnlyList<CompletionTrendPointDto> YearList);

public record CourseReportDto(
    int CourseId, string CourseCode, string CourseTitle, string Status,
    int AssignedCount, int CompletedCount, int InProgressCount, int OverdueCount,
    decimal CompletionRate, decimal AverageProgress, decimal? AverageScore, int CertificateCount,
    decimal? AverageRating, int ReviewCount);

public record DepartmentReportDto(
    int DepartmentId, string DepartmentCode, string DepartmentName,
    int UserCount, int AssignedCount, int CompletedCount,
    decimal CompletionRate, decimal? AverageScore);

public record LearnerReportDto(
    int UserId, string EmployeeCode, string FullName, string? DepartmentName, string? PositionName,
    int AssignedCount, int CompletedCount, int OverdueCount,
    decimal AverageProgress, decimal? AverageScore, int CertificateCount);

/// <summary>Số liệu tổng hợp hiển thị trên trang chủ công khai (không lộ dữ liệu cá nhân).</summary>
public record PublicStatDto(
    int PublishedCourse, int TotalLesson, int TotalQuiz,
    int CompletedEnrollment, int TotalCertificate, int ActiveLearner);

public record UploadResultDto(string FileName, string Url, long SizeByte, string ContentType);
