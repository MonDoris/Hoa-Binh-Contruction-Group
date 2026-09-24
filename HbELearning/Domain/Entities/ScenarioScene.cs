// File: Domain/Entities/ScenarioScene.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một kịch bản mô phỏng 3D (VD "An toàn lao động công trường") gắn với một khóa học.</summary>
public class ScenarioScene
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    /// <summary>Bài học mà mô phỏng này gắn theo (nếu có) — null = mô phỏng độc lập.</summary>
    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Bối cảnh 3D dùng để dựng cảnh (khớp key trong wwwroot/js/sim-registry.js) —
    /// VD "construction" = công trường xây dựng, "hr-office" = văn phòng phòng nhân sự.
    /// Đây là thứ quyết định người học nhìn thấy KHÔNG GIAN nào; nội dung tình huống vẫn do NodeList quy định.</summary>
    [Required, MaxLength(50)]
    public string SceneKey { get; set; } = "construction";

    /// <summary>Tệp bối cảnh do người soạn bài TỰ TẢI LÊN — chỉ dùng khi SceneKey = "custom".
    /// Nhờ nó, thêm một bối cảnh mới không còn phải viết thêm file dựng cảnh trong wwwroot/js/:
    /// nghiệp vụ tải lên model .glb / ảnh 360° / video 360° / ảnh thường rồi bấm thẳng lên đó để đặt
    /// vị trí từng tình huống. Null khi dùng các bối cảnh dựng sẵn (construction, hr-office...).</summary>
    [MaxLength(500)]
    public string? AssetUrl { get; set; }

    /// <summary>Loại tệp bối cảnh, quyết định cách dựng cảnh trong wwwroot/js/sim-asset-scene.js:
    /// "glb" = model 3D, "pano-image" = ảnh 360°, "pano-video" = video 360°, "image-2d" = ảnh thường.</summary>
    [MaxLength(20)]
    public string? AssetType { get; set; }

    /// <summary>Bắt buộc xử lý các bước theo đúng thứ tự OrderIndex (dùng cho mô phỏng QUY TRÌNH, VD quy
    /// trình tuyển dụng: không thể ký hợp đồng trước khi phỏng vấn). False = tự do khám phá, dùng cho mô
    /// phỏng kiểu KIỂM TRA HIỆN TRƯỜNG (VD dò điểm nguy cơ an toàn lao động, thứ tự không quan trọng).</summary>
    public bool IsSequential { get; set; }

    /// <summary>Điểm tối thiểu (%) trên tổng điểm tối đa để đạt mô phỏng.</summary>
    public decimal PassScore { get; set; } = 70m;

    /// <summary>Cỡ người trong cảnh này — hệ số nhân áp cho MỌI nhân vật được đặt vào.
    ///
    /// Phải có một con số riêng cho từng cảnh vì mọi model tải lên đều bị co về cùng một hộp bao 10 đơn vị
    /// bất kể kích thước thật (xem MODEL_SIZE trong js/sim-asset-scene.js): một văn phòng rộng 15 m và một
    /// nhà xưởng rộng 60 m sau chuẩn hóa đều là 10 đơn vị, nên cùng một người cao 1,7 m phải hiện ra với hai
    /// tỉ lệ chênh nhau bốn lần. Không có cách nào đoán đúng từ tệp — người soạn bài kéo thanh trượt canh
    /// người đứng cạnh cái bàn cho vừa mắt, một lần cho cả cảnh.</summary>
    public float CharacterScale { get; set; } = 1f;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ScenarioNode> NodeList { get; set; } = new List<ScenarioNode>();
    public ICollection<ScenarioAttempt> AttemptList { get; set; } = new List<ScenarioAttempt>();
    public ICollection<PathItem> PathItemList { get; set; } = new List<PathItem>();
    public ICollection<ScenarioCharacter> CharacterList { get; set; } = new List<ScenarioCharacter>();
}
