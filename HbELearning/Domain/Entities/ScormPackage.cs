// File: Domain/Entities/ScormPackage.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một gói nội dung SCORM (1.2 hoặc 2004) đã nhập vào một khóa học.
///
/// Tệp của gói được giải nén vào kho riêng <c>App_Data/scorm/{StorageKey}</c> và phục vụ ra ngoài ở
/// <c>/scorm-content/{StorageKey}/...</c>: nội dung SCORM là cả một website nhỏ (HTML, JS, ảnh, video) gọi lẫn
/// nhau bằng đường dẫn tương đối, nên không thể phát qua một API trả từng tệp như giấy chứng nhận. StorageKey là
/// GUID ngẫu nhiên 128 bit — cùng cách bảo vệ với tệp bài học ở /uploads.
///
/// <see cref="ActivityTreeJson"/> là cây hoạt động đã đọc từ imsmanifest.xml (tổ chức mặc định, kèm toàn bộ định
/// nghĩa sequencing của từng hoạt động). Đọc manifest một lần lúc nhập rồi lưu lại, thay vì phân tích XML ở mỗi lượt
/// học viên bấm "Tiếp" — engine sequencing cần cây này ở MỌI yêu cầu điều hướng.</summary>
public class ScormPackage
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Gói nhập từ ngoài, hay gói của chính khóa học (cây dựng từ cấu trúc khóa học).</summary>
    public ScormPackageKind Kind { get; set; } = ScormPackageKind.Imported;

    /// <summary>Lần dựng lại cây gần nhất (chỉ có nghĩa với gói của khóa học).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Tên hiển thị — mặc định lấy tiêu đề tổ chức (organization) trong manifest, người soạn sửa được.</summary>
    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    public ScormVersion Version { get; set; }

    /// <summary>Nguyên văn phiên bản khai trong manifest: "1.2", "2004 3rd Edition", "2004 4th Edition", "CAM 1.3"...</summary>
    [MaxLength(50)]
    public string? SchemaVersion { get; set; }

    /// <summary>Thuộc tính identifier của thẻ &lt;manifest&gt;.</summary>
    [MaxLength(250)]
    public string? ManifestIdentifier { get; set; }

    /// <summary>Tên thư mục trong kho SCORM (GUID dạng N, 32 ký tự hex).</summary>
    [Required, MaxLength(32)]
    public string StorageKey { get; set; } = null!;

    [MaxLength(255)]
    public string? OriginalFileName { get; set; }

    /// <summary>Dung lượng tệp .zip đã tải lên (byte).</summary>
    public long FileSize { get; set; }

    /// <summary>Tổng dung lượng sau khi giải nén (byte) và số tệp — để người soạn biết gói nặng tới đâu.</summary>
    public long ExtractedSize { get; set; }
    public int FileCount { get; set; }

    /// <summary>Số hoạt động lá có nội dung để phát (SCO + asset).</summary>
    public int LaunchableCount { get; set; }

    /// <summary>Số SCO (nội dung có giao tiếp với LMS qua API) — 0 nghĩa là gói chỉ toàn tài liệu tĩnh.</summary>
    public int ScoCount { get; set; }

    /// <summary>Thời lượng dự kiến (phút) — đọc từ typicalLearningTime trong metadata nếu có, người soạn sửa được.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Cây hoạt động đã phân tích (JSON) — xem Application/Features/Scorm/Model.</summary>
    [Required]
    public string ActivityTreeJson { get; set; } = null!;

    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<PathItem> PathItemList { get; set; } = new List<PathItem>();
    public ICollection<ScormRegistration> RegistrationList { get; set; } = new List<ScormRegistration>();
}
