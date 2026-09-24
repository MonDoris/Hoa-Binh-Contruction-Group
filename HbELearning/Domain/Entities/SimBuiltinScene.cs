// File: Domain/Entities/SimBuiltinScene.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một BỐI CẢNH DỰNG SẴN trong thư viện 3D — không gian được dựng bằng code trong wwwroot/js/
/// (VD "construction" = công trường xây dựng, "hr-office" = văn phòng phòng Nhân sự) chứ không phải một
/// tệp tải lên như <see cref="SimAsset"/>.
///
/// Trước đây hai bối cảnh này chỉ tồn tại dưới dạng key cứng trong js/sim-registry.js và một danh sách chép
/// tay trong pages/course-detail.html: người quản trị mở Thư viện 3D thì thấy thư viện trống trơn dù hệ thống
/// đang có sẵn hai không gian dùng được, còn muốn đổi tên hiển thị thì phải sửa mã nguồn. Bảng này đưa chúng
/// thành mục chính thức của thư viện, đứng ngang hàng với tệp tải lên.
///
/// TÁCH BẢNG RIÊNG (không nhét chung vào SimAsset) vì bối cảnh dựng sẵn KHÔNG có tệp: SimAsset.FileUrl là cột
/// bắt buộc và là thứ ScenarioScene.AssetUrl trỏ tới, nhét một bản ghi không tệp vào đó thì mọi truy vấn đang
/// đối chiếu theo đường dẫn đều phải thêm ngoại lệ. Ở đây khóa nhận dạng là <see cref="SceneKey"/>, đúng thứ
/// mà ScenarioScene.SceneKey lưu.
///
/// CÁI GÌ DỰNG ĐƯỢC vẫn do MÃ NGUỒN quyết định (SIM_SCENE_MAP trong js/sim-registry.js và SceneKeyList trong
/// ScenarioService) — bản ghi ở đây chỉ quyết định bối cảnh đó ĐƯỢC GỌI LÀ GÌ và mô tả ra sao. Vì vậy
/// không có API tạo mới/xóa: thêm một key ở đây mà không có mã dựng cảnh thì học viên chỉ nhận màn hình đen.</summary>
public class SimBuiltinScene
{
    public int Id { get; set; }

    /// <summary>Khóa bối cảnh, PHẢI khớp một mục trong SIM_SCENE_MAP của wwwroot/js/sim-registry.js và một
    /// phần tử của SceneKeyList trong Controllers/ScenarioService.cs. Đây cũng là giá trị lưu trong
    /// <see cref="ScenarioScene.SceneKey"/>, nên đổi chuỗi này là đổi bối cảnh của mọi mô phỏng đang dùng nó.</summary>
    [Required, MaxLength(50)]
    public string SceneKey { get; set; } = null!;

    /// <summary>Tên hiển thị trong Thư viện 3D và trong ô chọn bối cảnh lúc soạn mô phỏng. Sửa được, vì mỗi
    /// công ty gọi cùng một không gian bằng một cái tên khác nhau ("Công trường xây dựng" / "Hiện trường thi công").</summary>
    [Required, MaxLength(250)]
    public string Name { get; set; } = null!;

    /// <summary>Mô tả không gian có gì và hợp với loại kịch bản nào — chính là phần gợi ý hiện dưới ô chọn
    /// bối cảnh ở màn soạn mô phỏng.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Ảnh xem trước do trình xem 3D chụp lại (giống <see cref="SimAsset.ThumbnailUrl"/>). Null thì
    /// thư viện hiện icon theo loại.</summary>
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>Thứ tự hiện trong thư viện và trong ô chọn bối cảnh — nhỏ hơn đứng trước.</summary>
    public int OrderIndex { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
