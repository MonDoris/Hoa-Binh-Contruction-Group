// File: Domain/Entities/CourseTarget.cs
namespace HbELearning.Domain.Entities;

/// <summary>
/// Đối tượng đào tạo mục tiêu của khóa học — theo phòng ban và/hoặc vị trí công việc.
/// Cho phép Trainer xây dựng nội dung riêng cho từng nhóm nhân sự.
/// </summary>
public class CourseTarget
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? PositionId { get; set; }
    public Position? Position { get; set; }
}
