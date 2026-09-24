// File: Application/DTOs/OrganizationDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public record DepartmentDto(int Id, string Code, string Name, string? Description, bool IsActive, bool IsHrDepartment, int UserCount);

/// <summary>Mã phòng ban do hệ thống tự sinh từ tên (VD "Phòng Công nghệ thông tin" -> "CNTT"), không nhận từ client.</summary>
public class DepartmentRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = null!;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Đánh dấu đây là phòng Nhân sự (duyệt xuất bản cuối cùng cho mọi khóa học). Đặt true cho phòng
    /// này sẽ tự động bỏ đánh dấu ở phòng khác — chỉ có đúng 1 phòng Nhân sự tại một thời điểm.</summary>
    public bool IsHrDepartment { get; set; }
}

public record PositionDto(int Id, string Code, string Name, string? Description,
    int? DepartmentId, string? DepartmentName, bool IsActive, int UserCount);

/// <summary>Mã vị trí do hệ thống tự sinh dạng {MãPhòngBan}-{STT 2 chữ số} (VD "IT-01"), không nhận từ client.</summary>
public class PositionRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = null!;
    [MaxLength(500)] public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}
