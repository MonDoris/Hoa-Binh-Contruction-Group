// File: Application/DTOs/StudyReminderDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Một lời nhắc học tập của người dùng hiện tại.
///
/// RemindAt là lần bắn KẾ TIẾP (đã được đẩy tới sau mỗi lần bắn với lời nhắc lặp lại), còn LastSentAt là lần
/// đã báo gần nhất — giao diện dựa vào cặp này để nói "lần tới lúc ..." thay vì lặp lại mốc người dùng đặt.</summary>
public record StudyReminderDto(
    int Id, string Title, string? Note, DateTime RemindAt, string Repeat,
    bool SendEmail, bool IsActive, DateTime? LastSentAt,
    int? EnrollmentId, int? CourseId, string? CourseTitle);

/// <summary>Dữ liệu tạo/sửa một lời nhắc. EnrollmentId phải là bản ghi phân công của chính người gọi —
/// controller kiểm tra lại, không tin số gửi lên.</summary>
public class StudyReminderRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(400)]
    public string? Note { get; set; }

    [Required]
    public DateTime RemindAt { get; set; }

    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.None;

    public bool SendEmail { get; set; }

    public int? EnrollmentId { get; set; }
}
