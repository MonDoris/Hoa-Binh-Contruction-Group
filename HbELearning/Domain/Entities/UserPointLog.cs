// File: Domain/Entities/UserPointLog.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Một dòng nhật ký cộng điểm xếp hạng — mỗi hoạt động (hoàn thành bài học/bài kiểm tra/khóa học) chỉ được
/// cộng điểm đúng 1 lần, đảm bảo bằng ràng buộc duy nhất (UserId, Reason, SourceType, SourceId).
/// Tổng điểm của người dùng = tổng Points của các dòng thuộc UserId đó (không lưu cache).
/// </summary>
public class UserPointLog
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int Points { get; set; }
    public PointReason Reason { get; set; }

    /// <summary>Loại đối tượng phát sinh điểm, VD "Lesson"/"Quiz"/"Course".</summary>
    [Required, MaxLength(30)]
    public string SourceType { get; set; } = null!;

    /// <summary>Id của đối tượng phát sinh điểm (LessonId/QuizId/CourseId tương ứng SourceType).</summary>
    public int SourceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
