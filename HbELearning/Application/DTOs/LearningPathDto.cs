// File: Application/DTOs/LearningPathDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record LearningPathDto(int Id, int CourseId, string CourseTitle, string Name,
    string? Description, bool IsSequential, IReadOnlyList<PathItemDto> ItemList);

// ParentLessonId: với bước Quiz gắn theo một bài học ("con" của bài học đó) — id bài học cha, dùng để nhóm hiển thị liền nhau.
public record PathItemDto(int Id, string ItemType, int? LessonId, int? QuizId,
    string Title, int DurationMinutes, int OrderIndex, bool IsRequired,
    DateTime? StartDate, DateTime? EndDate, int? ParentLessonId, int? ScenarioSceneId);

public class LearningPathRequest
{
    [Required] public int CourseId { get; set; }
    [Required, MaxLength(250)] public string Name { get; set; } = null!;
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsSequential { get; set; } = true;
}

public class PathItemRequest
{
    [Required] public PathItemType ItemType { get; set; }
    public int? LessonId { get; set; }
    public int? QuizId { get; set; }
    public int? ScenarioSceneId { get; set; }
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>Sửa lịch mở/khóa theo thời gian của một bước trong lộ trình đã có sẵn.</summary>
public class PathItemScheduleRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsRequired { get; set; } = true;
}
