// File: Domain/Entities/PathItem.cs

namespace HbELearning.Domain.Entities;

/// <summary>Một mắt xích trong lộ trình — trỏ tới Lesson hoặc Quiz theo thứ tự OrderIndex.</summary>
public class PathItem
{
    public int Id { get; set; }

    public int LearningPathId { get; set; }
    public LearningPath LearningPath { get; set; } = null!;

    public PathItemType ItemType { get; set; }

    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public int? QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int? ScenarioSceneId { get; set; }
    public ScenarioScene? ScenarioScene { get; set; }

    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;

    /// <summary>Null = không giới hạn theo thời gian, chỉ xét tuần tự như cũ.</summary>
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
