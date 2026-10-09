// File: Application/DTOs/ScenarioDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

// ---------- Quản trị (Trainer/Admin) ----------
public record ScenarioSceneListDto(int Id, int CourseId, string Title, decimal PassScore,
    int NodeCount, bool IsActive, int? LessonId, string? LessonTitle, string SceneKey, bool IsSequential,
    string? AssetType);

public record ScenarioManageDto(int Id, int CourseId, string CourseTitle, string Title, string? Description,
    decimal PassScore, bool IsActive, int? LessonId, string? LessonTitle, string SceneKey, bool IsSequential,
    string? AssetUrl, string? AssetType,
    IReadOnlyList<ScenarioNodeManageDto> NodeList,
    float CharacterScale = 1f,
    IReadOnlyList<ScenarioCharacterDto>? CharacterList = null);

public record ScenarioNodeManageDto(int Id, string Title, string SituationText,
    float PositionX, float PositionY, float PositionZ, int OrderIndex, string? MediaUrl,
    IReadOnlyList<ScenarioChoiceManageDto> ChoiceList,
    int? AttachedCharacterId = null);

/// <summary>Một nhân vật đã đặt trong cảnh. Dùng chung cho cả màn soạn bài lẫn màn học: học viên phải nhìn
/// thấy y hệt những gì người soạn bài đã dựng, và ở đây không có gì cần giấu (khác với đáp án của tình
/// huống — xem chú thích ở phần DTO của học viên bên dưới).</summary>
public record ScenarioCharacterDto(int Id, string? Label, string CharacterKey, string PoseKey,
    float PositionX, float PositionY, float PositionZ, float RotationY, float Scale,
    string? PathJson, float WalkSpeed, int OrderIndex,
    string? OutfitColor = null,
    /// <summary>Đường dẫn tệp model của nhân vật, do server tra từ CharacterKey. Bộ cài sẵn trỏ vào
    /// wwwroot/models/character/, bộ do công ty tải lên Thư viện 3D trỏ vào wwwroot/uploads/sim-library/.
    /// Gửi kèm sẵn để trình dựng cảnh không phải gọi thêm một lượt API chỉ để hỏi "tệp nằm ở đâu".</summary>
    string? ModelUrl = null);

public record ScenarioChoiceManageDto(int Id, string Content, bool IsCorrect, int PointDelta,
    string? Feedback, int OrderIndex);

public class ScenarioSceneRequest
{
    [Required] public int CourseId { get; set; }

    /// <summary>Chương của cấu trúc khóa học sẽ chứa nội dung mới (chỉ dùng lúc tạo); null = tự xếp chỗ.</summary>
    public int? ParentItemId { get; set; }

    /// <summary>Bài học mà mô phỏng này gắn theo (nếu có) — null = mô phỏng độc lập.</summary>
    public int? LessonId { get; set; }

    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [MaxLength(1000)] public string? Description { get; set; }
    [Range(0, 100)] public decimal PassScore { get; set; } = 70m;
    public bool IsActive { get; set; } = true;

    /// <summary>Bối cảnh 3D (key trong wwwroot/js/sim-registry.js). Danh sách hợp lệ được kiểm tại ScenarioService.</summary>
    [Required, MaxLength(50)] public string SceneKey { get; set; } = "construction";

    /// <summary>Tệp bối cảnh tự tải lên — BẮT BUỘC khi SceneKey = "custom" (xem ScenarioService).</summary>
    [MaxLength(500)] public string? AssetUrl { get; set; }

    /// <summary>"glb" | "pano-image" | "pano-video" | "image-2d". Bắt buộc đi kèm AssetUrl.</summary>
    [MaxLength(20)] public string? AssetType { get; set; }

    /// <summary>True = mô phỏng quy trình, phải xử lý các bước lần lượt theo OrderIndex.</summary>
    public bool IsSequential { get; set; }

    /// <summary>Cỡ người trong cảnh — xem ScenarioScene.CharacterScale. Giới hạn trên/dưới đủ rộng để hợp
    /// với cả model đơn vị mét lẫn model đơn vị centimet, nhưng vẫn chặn số 0 (nhân vật biến mất, người
    /// soạn bài tưởng tính năng hỏng).</summary>
    [Range(0.01, 100)] public float CharacterScale { get; set; } = 1f;
}

public class ScenarioNodeRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [Required, MaxLength(2000)] public string SituationText { get; set; } = null!;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public int OrderIndex { get; set; }

    /// <summary>Ảnh/video minh họa hiển thị kèm tình huống (tùy chọn).</summary>
    [MaxLength(500)] public string? MediaUrl { get; set; }

    /// <summary>Gắn tình huống vào một nhân vật trong cảnh (tùy chọn) — xem ScenarioNode.AttachedCharacterId.</summary>
    public int? AttachedCharacterId { get; set; }

    [Required, MinLength(2, ErrorMessage = "Phải có ít nhất 2 lựa chọn")]
    public List<ScenarioChoiceRequest> ChoiceList { get; set; } = new();
}

public class ScenarioChoiceRequest
{
    [Required, MaxLength(500)] public string Content { get; set; } = null!;
    public bool IsCorrect { get; set; }
    public int PointDelta { get; set; }
    [MaxLength(1000)] public string? Feedback { get; set; }
    public int OrderIndex { get; set; }

    /// <summary>Ảnh/video minh họa hiển thị kèm tình huống (tùy chọn).</summary>
    [MaxLength(500)] public string? MediaUrl { get; set; }
}

// ---------- Chơi mô phỏng (Learner) — KHÔNG lộ IsCorrect/PointDelta/Feedback trước khi trả lời ----------
/// <param name="SceneName">Tên bối cảnh như Thư viện 3D đang đặt (bảng SimBuiltinScene). Gửi kèm để trang
/// học viên hiện đúng tên mà người quản trị đã đặt, thay vì chuỗi cứng trong js/sim-registry.js — học viên
/// không được gọi API thư viện nên không tự tra được. Null với bối cảnh tự tải lên ("custom").</param>
public record ScenarioStartDto(int AttemptId, int ScenarioSceneId, string Title, string? Description,
    int AttemptNo, decimal PassScore, int MaxScore, int Score, DateTime StartedAt,
    string SceneKey, bool IsSequential, string? AssetUrl, string? AssetType,
    IReadOnlyList<ScenarioNodeTakeDto> NodeList, IReadOnlyList<int> AnsweredNodeIdList,
    string? SceneName = null,
    float CharacterScale = 1f,
    IReadOnlyList<ScenarioCharacterDto>? CharacterList = null);

public record ScenarioNodeTakeDto(int Id, string Title, string SituationText, int OrderIndex,
    float PositionX, float PositionY, float PositionZ, string? MediaUrl,
    IReadOnlyList<ScenarioChoiceTakeDto> ChoiceList,
    int? AttachedCharacterId = null);

public record ScenarioChoiceTakeDto(int Id, string Content);

public class SubmitScenarioAnswerRequest
{
    [Required] public int NodeId { get; set; }
    [Required] public int ChoiceId { get; set; }
}

public record ScenarioAnswerResultDto(int NodeId, int ChoiceId, bool IsCorrect,
    int PointsEarned, int NewScore, string? Feedback, int CorrectChoiceId);

public record ScenarioResultDto(int AttemptId, int ScenarioSceneId, string Title, int AttemptNo,
    int Score, int MaxScore, decimal ScorePercent, decimal PassScore, bool IsPassed,
    DateTime StartedAt, DateTime? CompletedAt, IReadOnlyList<ScenarioResultItemDto> ItemList);

public record ScenarioResultItemDto(int NodeId, string NodeTitle, int ChoiceId, string ChoiceContent,
    bool IsCorrect, int PointsEarned, string? Feedback);

/// <summary>Dữ liệu tạo/sửa một nhân vật trong cảnh.</summary>
public class ScenarioCharacterRequest
{
    [MaxLength(150)] public string? Label { get; set; }

    /// <summary>Bộ nhân vật — danh sách hợp lệ do ScenarioService.CharacterSetList quy định.</summary>
    [Required, MaxLength(50)] public string CharacterKey { get; set; } = "office";

    /// <summary>Màu trang phục #rrggbb, để trống = hệ thống tự rải màu theo Id.</summary>
    [MaxLength(20)] public string? OutfitColor { get; set; }

    /// <summary>Tư thế — danh sách hợp lệ do ScenarioService.CharacterPoseList quy định.</summary>
    [Required, MaxLength(30)] public string PoseKey { get; set; } = "idle";

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    /// <summary>Hướng quay mặt, tính bằng độ. Không chặn khoảng: 370 độ và 10 độ là một, chặn lại chỉ tổ
    /// báo lỗi cho người kéo thanh trượt quá một vòng.</summary>
    public float RotationY { get; set; }

    [Range(0.05, 20)] public float Scale { get; set; } = 1f;

    /// <summary>Lối đi cho tư thế "walk", JSON [[x,y,z],...] — xem ScenarioCharacter.PathJson.</summary>
    [MaxLength(2000)] public string? PathJson { get; set; }

    [Range(0, 20)] public float WalkSpeed { get; set; }

    public int OrderIndex { get; set; }
}
