// File: Application/DTOs/LessonNoteDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Một ghi chú của học viên tại một mốc thời gian trong video.</summary>
public record LessonNoteDto(int Id, int LessonId, int AtSecond, string Content,
    DateTime CreatedAt, DateTime? UpdatedAt);

public class LessonNoteCreateRequest
{
    [Required] public int EnrollmentId { get; set; }
    [Required] public int LessonId { get; set; }

    /// <summary>Giây trong video. Chặn trên 360000 (100 giờ) giống mốc câu hỏi trong video — đủ rộng cho mọi
    /// bài giảng thật, đủ hẹp để một con số rác không lọt vào.</summary>
    [Range(0, 360000)] public int AtSecond { get; set; }

    [Required, MaxLength(2000)] public string Content { get; set; } = null!;
}

/// <summary>Sửa ghi chú. Chỉ cho sửa nội dung, KHÔNG cho sửa mốc thời gian: mốc là chỗ ghi chú này thuộc về,
/// đổi nó đi thì câu chữ còn lại không còn nói về đoạn video nào nữa. Muốn ghi cho đoạn khác thì viết ghi
/// chú mới tại đoạn đó.</summary>
public class LessonNoteUpdateRequest
{
    [Required, MaxLength(2000)] public string Content { get; set; } = null!;
}
