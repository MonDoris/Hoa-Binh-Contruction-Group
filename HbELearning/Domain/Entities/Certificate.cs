// File: Domain/Entities/Certificate.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Chứng nhận hoàn thành khóa học (quan hệ 1-1 với Enrollment).</summary>
public class Certificate
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    /// <summary>Số hiệu chứng nhận hiển thị cho người dùng. VD: HBE-2026-000123.</summary>
    [Required, MaxLength(50)]
    public string CertificateNo { get; set; } = null!;

    /// <summary>Mã ngẫu nhiên nhúng trong QR Code để xác thực công khai.</summary>
    [Required, MaxLength(64)]
    public string VerificationCode { get; set; } = null!;

    public DateTime IssuedAt { get; set; } = DateTime.Now;
    public DateTime? ExpiredAt { get; set; }

    public decimal FinalScore { get; set; }

    /// <summary>Đường dẫn ảnh QR đã sinh trong wwwroot/uploads/qr.</summary>
    [MaxLength(500)]
    public string? QrCodeUrl { get; set; }

    public bool IsRevoked { get; set; }
}
