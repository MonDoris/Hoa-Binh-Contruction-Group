// File: Domain/Entities/ScormRegistration.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Bản ghi học một gói SCORM của MỘT người (thuật ngữ "registration" của các LMS SCORM).
///
/// Giữ toàn bộ trạng thái sequencing của người học trên gói này trong <see cref="StateJson"/>: số lần thử và
/// trạng thái hoàn thành / đạt của TỪNG hoạt động, mục tiêu toàn cục, hoạt động đang tạm dừng, dữ liệu chia sẻ
/// (adl.data). Các cột còn lại là bản tóm tắt của hoạt động gốc, chép ra để báo cáo và lộ trình học đọc được mà
/// không phải giải JSON.
///
/// <see cref="EnrollmentId"/> null = bản XEM THỬ của người soạn / người duyệt: dữ liệu vẫn ghi đủ để nội dung chạy
/// như thật, nhưng không tính vào tiến độ, điểm hay chứng nhận của ai.</summary>
public class ScormRegistration
{
    public int Id { get; set; }

    public int? EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    /// <summary>Người học — chép từ Enrollment.UserId, hoặc người đang xem thử.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ScormPackageId { get; set; }
    public ScormPackage ScormPackage { get; set; } = null!;

    public bool IsPreview { get; set; }

    /// <summary>Trạng thái engine sequencing (JSON). Null = chưa học lần nào.</summary>
    public string? StateJson { get; set; }

    /// <summary>Phiên học đang giữ bản ghi này. Mỗi lần mở trình phát sinh phiên mới; yêu cầu mang phiên cũ (tab
    /// thứ hai, tab mở từ hôm trước) bị từ chối thay vì ghi chồng lên tiến độ của phiên mới.</summary>
    [MaxLength(32)]
    public string? SessionId { get; set; }

    /// <summary>Số lần đã bắt đầu một lượt học mới trên hoạt động gốc.</summary>
    public int AttemptCount { get; set; }

    /// <summary>"completed" | "incomplete" | "unknown" — của hoạt động gốc, sau rollup.</summary>
    [MaxLength(20)]
    public string CompletionStatus { get; set; } = "unknown";

    /// <summary>"passed" | "failed" | "unknown" — của hoạt động gốc, sau rollup.</summary>
    [MaxLength(20)]
    public string SuccessStatus { get; set; } = "unknown";

    /// <summary>Điểm chuẩn hóa của hoạt động gốc, -1..1. Null = gói không báo điểm.</summary>
    public decimal? ScoreScaled { get; set; }

    /// <summary>Điểm chuẩn hóa cao nhất từng đạt qua mọi lượt học — chép sang lộ trình làm "điểm cao nhất".</summary>
    public decimal? BestScoreScaled { get; set; }

    /// <summary>Tổng thời gian học thật (giây) cộng dồn qua mọi phiên.</summary>
    public int TotalTimeSecond { get; set; }

    /// <summary>Đang tạm dừng giữa chừng (suspend) — lần mở sau sẽ học tiếp đúng chỗ.</summary>
    public bool IsSuspended { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>Lần ĐẦU hoạt động gốc đạt hoàn thành mà không bị đánh trượt. Một khi đã có thì giữ nguyên — học
    /// lại từ đầu (lượt mới xóa trạng thái hoàn thành của lượt cũ) không được kéo bước lộ trình về "chưa xong".</summary>
    public DateTime? CompletedAt { get; set; }

    public ICollection<ScormRuntime> RuntimeList { get; set; } = new List<ScormRuntime>();
}
