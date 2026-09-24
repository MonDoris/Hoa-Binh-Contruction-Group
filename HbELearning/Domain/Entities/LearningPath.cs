// File: Domain/Entities/LearningPath.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Lộ trình học tập của một khóa học (quan hệ 1-1 với Course).</summary>
public class LearningPath
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    [Required, MaxLength(250)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>True = bắt buộc học đúng thứ tự, phải hoàn thành mục trước mới mở mục sau.</summary>
    public bool IsSequential { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<PathItem> PathItemList { get; set; } = new List<PathItem>();
}
