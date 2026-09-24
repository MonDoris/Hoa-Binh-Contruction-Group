// File: Domain/Entities/LessonNote.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một ghi chú của học viên gắn với ĐÚNG một mốc thời gian trong video bài học.
///
/// Vì sao phải gắn mốc thời gian thay vì cho gõ tự do một ô ghi chú chung cho cả bài: giá trị của ghi chú
/// khi ôn lại nằm ở chỗ quay về đúng đoạn đã ghi. Một cuốn sổ tay dài không mốc thì đến lúc ôn, học viên
/// đọc lại dòng "chỗ này quan trọng" mà không còn biết "chỗ này" là phút thứ mấy — và phải tua lại cả video
/// để tìm, tức là mất đúng cái thời gian mà ghi chú lẽ ra phải tiết kiệm cho họ.
///
/// Gắn theo EnrollmentId (không phải UserId) cho thống nhất với tiến độ học và câu trả lời trong video —
/// mọi thứ thuộc về "lần học khóa này" đều treo vào bản ghi phân công, nên kiểm tra quyền chỉ có một kiểu
/// và xóa phân công là dọn sạch theo, không để lại dữ liệu mồ côi.</summary>
public class LessonNote
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    /// <summary>Giây trong video lúc học viên bấm ghi chú.</summary>
    public int AtSecond { get; set; }

    /// <summary>Giới hạn 2000 ký tự: đây là ghi chú cho một khoảnh khắc, dài hơn thế thì nó đã là bài viết
    /// và mốc thời gian mất ý nghĩa.</summary>
    [Required, MaxLength(2000)]
    public string Content { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>null = chưa sửa lần nào. Giao diện dựa vào đó để hiện "đã sửa" — người học cần phân biệt
    /// câu mình viết lúc đang xem với câu đã viết lại sau khi hiểu ra vấn đề.</summary>
    public DateTime? UpdatedAt { get; set; }
}
