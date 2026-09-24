// File: Domain/Entities/Department.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Department
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Đánh dấu đây là phòng Nhân sự — Giám Đốc của phòng này là người duyệt xuất bản cuối cùng cho
    /// mọi khóa học (mọi phòng ban), bất kể mã phòng ban là gì. Chỉ nên có đúng 1 phòng được đánh dấu.</summary>
    public bool IsHrDepartment { get; set; }

    public ICollection<Position> PositionList { get; set; } = new List<Position>();
    public ICollection<User> UserList { get; set; } = new List<User>();
    public ICollection<CourseTarget> CourseTargetList { get; set; } = new List<CourseTarget>();
}
