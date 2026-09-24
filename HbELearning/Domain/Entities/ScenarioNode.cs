// File: Domain/Entities/ScenarioNode.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một điểm nguy cơ (hazard) trong cảnh 3D — vị trí + tình huống + các lựa chọn xử lý.</summary>
public class ScenarioNode
{
    public int Id { get; set; }

    public int ScenarioSceneId { get; set; }
    public ScenarioScene ScenarioScene { get; set; } = null!;

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Mô tả tình huống hiển thị khi học viên chạm vào điểm nguy cơ này.</summary>
    [Required, MaxLength(2000)]
    public string SituationText { get; set; } = null!;

    /// <summary>Tọa độ 3D (khớp với cảnh dựng bằng Three.js ở trang mô phỏng).</summary>
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    public int OrderIndex { get; set; }

    /// <summary>Gắn tình huống này vào một NHÂN VẬT trong cảnh thay vì một điểm cố định.
    ///
    /// Có giá trị thì dấu chấm than bám theo người đó — kể cả khi người đó đang đi lại theo lối đi đã đặt —
    /// và ba ô tọa độ ở trên không còn được dùng tới. Null = điểm cố định như cũ.
    ///
    /// Không khai báo khóa ngoại thật tới ScenarioCharacter: cả hai bảng đều đã xóa lan truyền từ
    /// ScenarioScene, thêm một đường lan truyền thứ hai là SqlServer từ chối tạo bảng ("multiple cascade
    /// paths"). Việc dọn tham chiếu khi xóa nhân vật do ScenarioService lo.</summary>
    public int? AttachedCharacterId { get; set; }

    /// <summary>Ảnh/video minh họa hiển thị kèm tình huống (tùy chọn) — VD ảnh chụp thao tác đúng, video
    /// hướng dẫn một bước quy trình. Là URL trả về từ UploadService, null = không có minh họa.</summary>
    [MaxLength(500)]
    public string? MediaUrl { get; set; }

    public ICollection<ScenarioChoice> ChoiceList { get; set; } = new List<ScenarioChoice>();
}
