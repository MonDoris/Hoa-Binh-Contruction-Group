// File: Domain/Entities/Position.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Position
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<User> UserList { get; set; } = new List<User>();
    public ICollection<CourseTarget> CourseTargetList { get; set; } = new List<CourseTarget>();
}
