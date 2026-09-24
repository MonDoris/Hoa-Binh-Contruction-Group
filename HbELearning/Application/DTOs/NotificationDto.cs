// File: Application/DTOs/NotificationDto.cs
namespace HbELearning.Application.DTOs;

public record NotificationDto(
    int Id, string Type, string Title, string Message, bool IsRead, DateTime CreatedAt,
    int? EnrollmentId, int? CourseId, string? CourseTitle);

public record UnreadCountDto(int Count);
