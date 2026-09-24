// File: Domain/Entities/CourseApprovalHistory.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một bước trong lộ trình duyệt nội dung khóa học (gửi duyệt / duyệt / từ chối) — dùng làm lịch sử/audit trail.</summary>
public class CourseApprovalHistory
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public int ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    /// <summary>Tên vai trò của người thực hiện bước này (VD "Trưởng Nhóm", "Trưởng phòng", "Giám Đốc").</summary>
    [MaxLength(50)]
    public string ActorRoleName { get; set; } = null!;

    /// <summary>Tên bước của LUỒNG CẤP DUYỆT mà thao tác này thuộc về (VD "Trưởng phòng duyệt") — null với
    /// khóa học chạy chuỗi duyệt cố định cũ, vốn đã đọc được cấp duyệt qua FromStatus/ToStatus.</summary>
    [MaxLength(100)]
    public string? StepName { get; set; }

    public CourseStatus FromStatus { get; set; }
    public CourseStatus ToStatus { get; set; }

    /// <summary>Lý do — bắt buộc khi ToStatus = Rejected, null với các bước gửi duyệt/duyệt lên cấp kế tiếp.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
