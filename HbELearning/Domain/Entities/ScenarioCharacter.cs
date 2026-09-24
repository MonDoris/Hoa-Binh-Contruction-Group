// File: Domain/Entities/ScenarioCharacter.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Một NHÂN VẬT được đặt vào cảnh 3D của mô phỏng — vị trí, hướng nhìn và tư thế đang diễn.
///
/// Vì sao cần bảng này: các cảnh dựng bằng code (công trường, văn phòng nhân sự) có sẵn người nhưng vị trí
/// nằm cứng trong mã nguồn, còn cảnh do nghiệp vụ TỰ TẢI LÊN (model .glb mua/tải về) thì luôn là phòng ốc
/// trống không có ai. Một văn phòng không bóng người khó dùng để dạy tình huống ứng xử: người học không
/// thấy được ai đang làm gì, ở đâu. Bảng này cho người soạn bài đặt người vào cảnh ngay trên giao diện,
/// không cần lập trình viên sửa code cho từng cảnh.
///
/// TƯ THẾ là tên một đoạn animation có sẵn trong tệp nhân vật (xem wwwroot/models/README.txt) chứ không
/// phải một phép biến hình tính bằng code: xoay tay chân bằng công thức cho ra dáng người cứng như ma-nơ-canh,
/// còn animation quay từ diễn viên thật thì ngồi ra ngồi, uống ra uống.
/// </summary>
public class ScenarioCharacter
{
    public int Id { get; set; }

    public int ScenarioSceneId { get; set; }
    public ScenarioScene ScenarioScene { get; set; } = null!;

    /// <summary>Tên gọi để người soạn bài nhận ra nhân vật trong danh sách (VD "Lễ tân", "Ứng viên chờ
    /// phỏng vấn"). Không hiện cho học viên — học viên chỉ thấy hình người trong cảnh.</summary>
    [MaxLength(150)]
    public string? Label { get; set; }

    /// <summary>Bộ nhân vật — khớp CHARACTER_SET_LIST trong wwwroot/js/sim-characters.js.
    ///
    /// Một "bộ" không nhất thiết là một tệp model riêng: bộ "office" và bộ "worker" dùng chung một tệp,
    /// khác nhau ở chỗ bộ văn phòng ẩn mũ bảo hộ và áo phản quang đi. Nhân vật Mixamo để mỗi món trang phục
    /// ở một mesh riêng nên bật/tắt được từng món — nhờ vậy có ngay hai kiểu người mà không phải đi tìm
    /// thêm model nào.
    ///
    /// Giá trị "staff" là của dữ liệu cũ (trước khi có khái niệm bộ) và được hiểu là bộ văn phòng.</summary>
    [Required, MaxLength(50)]
    public string CharacterKey { get; set; } = "office";

    /// <summary>Màu trang phục dạng #rrggbb. Null = tự chọn theo Id nhân vật.
    ///
    /// Phải có thứ này vì cả cảnh dùng chung MỘT tệp model: không đổi màu thì mười người trong phòng là mười
    /// bản sao giống hệt nhau tới từng nếp áo, nhìn vào biết ngay là hình dán chứ không phải người. Để trống
    /// thì hệ thống tự rải màu theo Id — người soạn bài không phải bận tâm, mà phòng vẫn ra dáng có nhiều
    /// người khác nhau.</summary>
    [MaxLength(20)]
    public string? OutfitColor { get; set; }

    /// <summary>Tư thế đang diễn — khớp TÊN ĐOẠN ANIMATION trong tệp nhân vật: idle (đứng), sit (ngồi),
    /// walk (đi), drink (uống), eat (ăn), talk (nói chuyện)... Danh sách hợp lệ do
    /// ScenarioService.CharacterPoseList quy định.
    ///
    /// Tư thế nào tệp nhân vật CHƯA có đoạn animation tương ứng thì lúc dựng cảnh sẽ rơi về "idle" thay vì
    /// làm nhân vật đứng chết cứng ở tư thế chữ T — xem js/sim-characters.js.</summary>
    [Required, MaxLength(30)]
    public string PoseKey { get; set; } = "idle";

    /// <summary>Vị trí trong cảnh, cùng hệ tọa độ với <see cref="ScenarioNode"/> (đã chuẩn hóa về hộp bao
    /// 10 đơn vị, đáy cảnh tại y = 0 — xem MODEL_SIZE trong js/sim-asset-scene.js).</summary>
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    /// <summary>Hướng nhân vật quay mặt, tính bằng ĐỘ quanh trục đứng. Dùng độ chứ không dùng radian vì
    /// đây là con số người soạn bài nhìn thấy và gõ tay trên giao diện.</summary>
    public float RotationY { get; set; }

    /// <summary>Hệ số phóng to riêng của nhân vật này, nhân thêm vào
    /// <see cref="ScenarioScene.CharacterScale"/> của cả cảnh. Để 1 trong hầu hết trường hợp; chỉ chỉnh khi
    /// cần một người cao/thấp khác hẳn số còn lại.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Lối đi của tư thế "walk", dạng JSON danh sách điểm: [[x,y,z],[x,y,z],...].
    ///
    /// Không có lối đi thì clip đi bộ vẫn chạy nhưng nhân vật đứng yên một chỗ — trông đúng như đang đi trên
    /// máy chạy bộ. Có lối đi thì nhân vật đi hết lượt rồi quay đầu đi ngược lại, và tự xoay mặt theo hướng
    /// đang đi.
    ///
    /// Lưu JSON thay vì thêm hẳn một bảng điểm-lối-đi: danh sách này luôn được đọc/ghi TRỌN GÓI cùng nhân
    /// vật, không bao giờ cần truy vấn hay thống kê riêng từng điểm.</summary>
    [MaxLength(2000)]
    public string? PathJson { get; set; }

    /// <summary>Tốc độ đi, tính bằng đơn vị cảnh trên giây. 0 = dùng mặc định của trình dựng cảnh.</summary>
    public float WalkSpeed { get; set; }

    public int OrderIndex { get; set; }
}
