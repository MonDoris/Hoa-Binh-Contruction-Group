// File: Domain/Entities/User.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string EmployeeCode { get; set; } = null!;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = null!;

    [Required, MaxLength(150)]
    public string Email { get; set; } = null!;

    /// <summary>Tên tài khoản dùng để đăng nhập THAY CHO email (tùy chọn). Luôn lưu ở dạng chữ thường và
    /// không chứa ký tự "@" — nhờ vậy một chuỗi người dùng gõ ở ô đăng nhập không bao giờ vừa là email
    /// của người này vừa là tên tài khoản của người khác. Null = tài khoản chỉ đăng nhập bằng email.</summary>
    [MaxLength(50)]
    public string? UserName { get; set; }

    [Required, MaxLength(255)]
    public string PasswordHash { get; set; } = null!;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>Ảnh bìa ở trang "Tài khoản của tôi" — luôn là tệp trong /uploads/cover/, null = ảnh bìa mặc định.</summary>
    [MaxLength(500)]
    public string? CoverUrl { get; set; }
    [MaxLength(1000)]
    public string? FunctionKeys { get; set; }

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? PositionId { get; set; }
    public Position? Position { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Số lần gõ sai mật khẩu LIÊN TIẾP kể từ lần đăng nhập thành công gần nhất. Về 0 ngay khi
    /// đăng nhập đúng — nên ba lần sai rải rác trong nhiều tháng không bao giờ cộng dồn thành một lần khóa.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Thời điểm tài khoản bị khóa vì gõ sai quá số lần cho phép (xem AuthService.MaxLoginAttempt).
    /// Null = chưa bị khóa. Cố ý KHÔNG tự mở lại sau một khoảng thời gian: yêu cầu nghiệp vụ là người dùng
    /// phải liên hệ phòng IT, nên chỉ quản trị viên mở khóa được (nút "Mở khóa" hoặc "Reset MK" ở màn hình
    /// Người dùng). Tách riêng khỏi <see cref="IsActive"/> để IT phân biệt được "khóa do gõ sai" với
    /// "vô hiệu hóa do nghỉ việc".</summary>
    public DateTime? LockedOutAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Course> CreatedCourseList { get; set; } = new List<Course>();
    public ICollection<Enrollment> EnrollmentList { get; set; } = new List<Enrollment>();
    public ICollection<Enrollment> AssignedEnrollmentList { get; set; } = new List<Enrollment>();
}
