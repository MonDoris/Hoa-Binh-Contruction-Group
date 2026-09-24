// File: Application/DTOs/LessonDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record LessonListDto(int Id, int CourseId, string Title, string ContentType,
    string? ResourceUrl, int DurationMinutes, int OrderIndex, bool IsActive,
    int? QuizId, string? QuizTitle,
    // Mô phỏng 3D gắn theo bài học — cho phép người tạo nội dung nhìn ngay bài học nào "có mô phỏng".
    int? ScenarioSceneId, string? ScenarioTitle, int ScenarioNodeCount);

public record LessonDetailDto(int Id, int CourseId, string CourseTitle, string Title, string? Content,
    string ContentType, string? ResourceUrl, int DurationMinutes, int OrderIndex, bool IsActive,
    string VideoSeekPolicy);

public class LessonRequest
{
    [Required] public int CourseId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    public string? Content { get; set; }
    public LessonContentType ContentType { get; set; } = LessonContentType.Text;
    [MaxLength(500)] public string? ResourceUrl { get; set; }
    [Range(0, 100000)] public int DurationMinutes { get; set; }

    /// <summary>Chỉ áp dụng cho bài học Video — bỏ trống thì giữ mặc định "tua tự do".</summary>
    public VideoSeekPolicy VideoSeekPolicy { get; set; } = VideoSeekPolicy.Free;

    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
}

public record ReorderItem(int Id, int OrderIndex);

/// <summary>Tạo bài học kèm (tùy chọn) một bài kiểm tra gắn ngay theo sau — "bài kiểm tra là con của bài học".</summary>
public class LessonWithQuizRequest
{
    [Required] public LessonRequest Lesson { get; set; } = null!;

    /// <summary>null = không tạo bài kiểm tra kèm theo.</summary>
    public QuizInfoRequest? Quiz { get; set; }
}

public class QuizInfoRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [Range(0, 100)] public decimal PassScore { get; set; } = 70m;
    [Range(1, 600)] public int TimeLimitMinutes { get; set; } = 20;
    [Range(1, 20)] public int MaxAttempt { get; set; } = 3;
}

public record LessonWithQuizResponse(int LessonId, int? QuizId, string Message);
