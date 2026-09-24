// File: Domain/Entities/SimAsset.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một tệp bối cảnh 3D trong THƯ VIỆN DÙNG CHUNG của công ty (model .glb, ảnh/video 360°, ảnh phẳng).
///
/// Trước khi có bảng này, mỗi mô phỏng phải tự tải tệp của riêng nó lên: cùng một model nhà xưởng dùng cho
/// 5 khóa học là 5 bản sao nằm rải rác trong wwwroot/uploads, đổi model thì phải sửa đủ 5 nơi, và người soạn
/// bài mới không có cách nào biết công ty đã có sẵn model nào. Thư viện giải quyết đúng chỗ đó: tải lên MỘT
/// lần, mọi mô phỏng chọn lại từ danh sách.
///
/// <see cref="ScenarioScene.AssetUrl"/> vẫn giữ nguyên kiểu chuỗi đường dẫn (không đổi thành khóa ngoại tới
/// bảng này) vì hai lý do: mô phỏng cũ đã lưu đường dẫn tệp tự tải lên vẫn phải chạy được, và người soạn bài
/// vẫn được phép tải một tệp dùng riêng cho đúng một mô phỏng mà không phải đưa vào thư viện chung.</summary>
public class SimAsset
{
    public int Id { get; set; }

    /// <summary>Tên gọi trong thư viện (VD "Cẩu tháp Potain MDT 219") — đây là thứ người soạn bài nhìn thấy
    /// khi chọn, KHÔNG phải tên tệp gốc vốn thường là một chuỗi vô nghĩa kiểu "scan_final_v3.glb".</summary>
    [Required, MaxLength(250)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Nhóm phân loại tự đặt (VD "Máy móc", "Nhà xưởng", "PCCC") — để lọc khi thư viện đã nhiều tệp.</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Loại tệp, khớp <see cref="ScenarioScene.AssetType"/> và AssetTypeList trong ScenarioService:
    /// "glb" = model 3D, "pano-image" = ảnh 360°, "pano-video" = video 360°, "image-2d" = ảnh thường.</summary>
    [Required, MaxLength(20)]
    public string AssetType { get; set; } = "glb";

    /// <summary>Đường dẫn tệp trong wwwroot (VD /uploads/sim-library/202608/xxx.glb) — chính là giá trị
    /// được gán vào ScenarioScene.AssetUrl khi một mô phỏng chọn tệp này.</summary>
    [Required, MaxLength(500)]
    public string FileUrl { get; set; } = null!;

    /// <summary>Tên tệp người dùng đã chọn lúc tải lên — giữ lại để tải về đúng tên và để đối chiếu bản cập nhật.</summary>
    [MaxLength(255)]
    public string? OriginalFileName { get; set; }

    public long FileSizeByte { get; set; }

    /// <summary>Ảnh xem trước, do trình xem 3D ở trình duyệt chụp lại khung hình rồi gửi lên (xem
    /// SimAssetService.SaveThumbnail). Null thì danh sách hiện icon theo loại tệp.
    /// Có ảnh thật quan trọng hơn vẻ đẹp: một thư viện toàn tên chữ thì phải mở từng tệp mới biết là cái gì.</summary>
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
