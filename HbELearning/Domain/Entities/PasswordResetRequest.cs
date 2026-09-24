// File: Domain/Entities/PasswordResetRequest.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một lượt quên mật khẩu: mã OTP gửi qua Zalo ZNS, sau khi xác thực đúng sẽ đổi lấy token để đặt mật khẩu mới.</summary>
public class PasswordResetRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(255)]
    public string OtpCodeHash { get; set; } = null!;

    public DateTime OtpExpiresAt { get; set; }
    public int OtpAttempts { get; set; }
    public bool IsOtpVerified { get; set; }

    [MaxLength(255)]
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetTokenExpiresAt { get; set; }

    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
