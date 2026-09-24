// File: Domain/Entities/Notification.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Thông báo / nhắc nhở học tập gửi cho một người dùng.</summary>
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Bản ghi phân công liên quan (nếu có) — dùng để liên kết tới khóa học khi bấm vào thông báo.</summary>
    public int? EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    /// <summary>Khóa học liên quan (nếu có) — dùng cho thông báo về chuỗi duyệt nội dung, không đi qua Enrollment.</summary>
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    public NotificationType Type { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
