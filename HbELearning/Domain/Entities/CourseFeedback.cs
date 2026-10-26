// File: Domain/Entities/CourseFeedback.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một phản hồi của người học về một khóa học: bình luận khi gặp vấn đề (kèm các trả lời trong cùng
/// luồng), hoặc đánh giá sao sau khi hoàn thành khóa.
///
/// Bình luận gốc và đánh giá gắn với EnrollmentId của người học (xóa phân công thì dọn theo, giống LessonNote).
/// Trả lời của người soạn khóa không có phân công nào nên EnrollmentId để trống — vì thế cột này nullable.
///
/// Content nullable vì đánh giá sao được phép không kèm chữ, mà Oracle coi chuỗi rỗng là NULL.</summary>
public class CourseFeedback
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Người viết.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    /// <summary>Trỏ tới bình luận gốc nếu đây là một trả lời. Chỉ một cấp: trả lời không có trả lời con.</summary>
    public int? ParentId { get; set; }
    public CourseFeedback? Parent { get; set; }

    public FeedbackKind Kind { get; set; }

    [MaxLength(2000)]
    public string? Content { get; set; }

    /// <summary>1–5 sao. Chỉ có ở Kind = Review.</summary>
    public int? Rating { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
