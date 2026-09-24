// File: Application/DTOs/ReportDto.cs
namespace HbELearning.Application.DTOs;

public record OverviewReportDto(
    int TotalUser, int ActiveUser, int TotalCourse, int PublishedCourse,
    int TotalEnrollment, int CompletedEnrollment, int InProgressEnrollment,
    int NotStartedEnrollment, int OverdueEnrollment,
    int TotalCertificate, decimal CompletionRate, decimal AverageProgress);

public record CourseReportDto(
    int CourseId, string CourseCode, string CourseTitle, string Status,
    int AssignedCount, int CompletedCount, int InProgressCount, int OverdueCount,
    decimal CompletionRate, decimal AverageProgress, decimal? AverageScore, int CertificateCount);

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
