// File: Domain/Entities/ExternalCertificate.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Chứng nhận nhân viên lấy được khi học ở BÊN NGOÀI công ty (trường, trung tâm, hãng...), do chính
/// nhân viên chụp giấy bằng camera hoặc tải bản PDF lên.
///
/// Tách bảng riêng thay vì dùng chung <see cref="Certificate"/>: chứng nhận nội bộ là thứ HỆ THỐNG cấp — gắn với
/// một lượt học (Enrollment), có điểm, có mã QR xác thực công khai. Giấy bên ngoài thì ngược lại: người dùng tự
/// khai, không có lượt học nào đứng sau, và chỉ đáng tin sau khi được đối chiếu (<see cref="Status"/>).
///
/// Người đối chiếu là AI (model đọc ảnh chạy nội bộ): AI đọc giấy rồi so với thông tin nhân viên khai và họ tên
/// tài khoản, khớp thì xác nhận, lệch thì từ chối kèm lý do — không ai phải ngồi duyệt. Admin vẫn xác nhận / thu
/// hồi tay được để sửa chỗ AI đọc sai (<see cref="IsAiReviewed"/> = false sau lần sửa đó).
///
/// Tệp quét nằm trong kho RIÊNG ngoài wwwroot (<see cref="FilePath"/>), chỉ đọc được qua API có kiểm quyền: giấy
/// chứng nhận mang họ tên, ngày sinh, số định danh — để ở /uploads thì ai có đường dẫn cũng mở được.</summary>
public class ExternalCertificate
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Tên chứng nhận / khóa học, VD "An toàn lao động nhóm 3".</summary>
    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Đơn vị cấp, VD "Trung tâm Huấn luyện An toàn lao động TP.HCM".</summary>
    [Required, MaxLength(250)]
    public string Issuer { get; set; } = null!;

    /// <summary>Số hiệu in trên giấy (nếu có) — để Admin đối chiếu và để tra lại khi cần.</summary>
    [MaxLength(100)]
    public string? CertificateNo { get; set; }

    /// <summary>Ngày cấp / ngày hết hạn ghi trên giấy, chỉ lưu phần ngày.</summary>
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }

    /// <summary>Ghi chú của nhân viên khi gửi (VD "Bản gốc nộp phòng Nhân sự ngày 05/10").</summary>
    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>Đường dẫn TƯƠNG ĐỐI trong kho tệp riêng — xem IFileService.SavePrivateAsync.</summary>
    [Required, MaxLength(500)]
    public string FilePath { get; set; } = null!;

    /// <summary>Tên tệp gốc người dùng chọn, hoặc tên tự đặt cho ảnh chụp từ camera.</summary>
    [Required, MaxLength(255)]
    public string FileName { get; set; } = null!;

    /// <summary>Kiểu tệp do MÁY CHỦ xác định từ nội dung (chữ ký đầu tệp), không phải do trình duyệt khai —
    /// đây là giá trị trả lại trong header Content-Type khi xem tệp.</summary>
    [Required, MaxLength(100)]
    public string ContentType { get; set; } = null!;

    public long FileSize { get; set; }

    public ExternalCertificateStatus Status { get; set; } = ExternalCertificateStatus.Pending;

    /// <summary>Lý do từ chối — bắt buộc khi Status = Rejected, để nhân viên biết phải gửi lại thứ gì.</summary>
    [MaxLength(1000)]
    public string? RejectReason { get; set; }

    public int? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Quyết định hiện tại là của AI (true) hay của Admin (false). Giấy AI chốt thì <see cref="ReviewedById"/>
    /// để trống — không có người nào đứng tên.</summary>
    public bool IsAiReviewed { get; set; }

    /// <summary>Những gì AI đọc được trên giấy và kết quả so từng mục (JSON) — để nhân viên biết vì sao bị từ
    /// chối, và Admin thấy AI đã dựa vào đâu mà xác nhận.</summary>
    public string? AiResultJson { get; set; }

    /// <summary>Số lần AI đọc giấy mà không ra kết quả dùng được (trả về rác, quá hạn chờ). Đếm để thôi thử lại
    /// sau vài lần — máy chủ AI TẮT thì không tính, lúc đó cứ chờ tới khi bật lại.</summary>
    public int AiAttemptCount { get; set; }

    // ==================== HÀNH VI: ĐỐI CHIẾU ====================
    // AI chỉ chốt giấy đang chờ, và chỉ chốt một lần. Admin thì sửa được quyết định đã có (AI đọc sai), nhưng
    // không "chốt lại" đúng trạng thái đang có: hai Admin cùng bấm, người bấm sau nhận lỗi chứ không lặng lẽ ghi đè.

    public bool IsPending => Status == ExternalCertificateStatus.Pending;

    public void ApproveByAi(DateTime now)
    {
        EnsurePending();
        Decide(ExternalCertificateStatus.Approved, null, null, isAi: true, now);
    }

    public void RejectByAi(string reason, DateTime now)
    {
        EnsurePending();
        Decide(ExternalCertificateStatus.Rejected, RequireReason(reason), null, isAi: true, now);
    }

    public void Approve(int reviewerId, DateTime now)
    {
        EnsureNot(ExternalCertificateStatus.Approved);
        Decide(ExternalCertificateStatus.Approved, null, reviewerId, isAi: false, now);
    }

    public void Reject(int reviewerId, string reason, DateTime now)
    {
        EnsureNot(ExternalCertificateStatus.Rejected);
        Decide(ExternalCertificateStatus.Rejected, RequireReason(reason), reviewerId, isAi: false, now);
    }

    private void Decide(ExternalCertificateStatus status, string? reason, int? reviewerId, bool isAi, DateTime now)
    {
        Status = status;
        RejectReason = reason;
        ReviewedById = reviewerId;
        IsAiReviewed = isAi;
        ReviewedAt = now;
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Phải có lý do từ chối.", nameof(reason));
        var trimmed = reason.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..999] + "…";
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new InvalidOperationException($"Chứng nhận đã được xử lý ({Status}).");
    }

    private void EnsureNot(ExternalCertificateStatus status)
    {
        if (Status == status)
            throw new InvalidOperationException($"Chứng nhận đã ở trạng thái {Status}.");
    }
}
