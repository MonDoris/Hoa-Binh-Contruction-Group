// File: Domain/Entities/AppLanguage.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một ngôn ngữ giao diện của hệ thống.
///
/// Trước đây danh sách ngôn ngữ nằm cứng trong LOCALE_TAG của wwwroot/js/language.js: thêm tiếng Nhật cho
/// một dự án là phải sửa mã nguồn rồi triển khai lại. Có bảng này thì màn hình "Dịch ngôn ngữ" tự thêm được,
/// và cả nút chuyển ngôn ngữ lẫn màn hình dịch đều tự có thêm mục mới.
///
/// Bảng chỉ giữ THÔNG TIN VỀ ngôn ngữ; câu chữ vẫn nằm ở <see cref="Translation"/>, đối chiếu qua
/// <see cref="Code"/> = Translation.Lang.</summary>
public class AppLanguage
{
    public int Id { get; set; }

    /// <summary>Mã ngôn ngữ ("vi", "en", "ja") — đúng bằng tên file trong wwwroot/locales và bằng
    /// <see cref="Translation.Lang"/>. Đây là khóa nhận dạng thật sự, đổi nó là mất liên kết với toàn bộ
    /// câu chữ đã dịch.</summary>
    [Required, MaxLength(10)]
    public string Code { get; set; } = null!;

    /// <summary>Tên hiển thị trong ô chọn ngôn ngữ, viết bằng CHÍNH ngôn ngữ đó ("Tiếng Việt", "English",
    /// "日本語") — người nói tiếng đó phải nhận ra được tên của mình trong danh sách.</summary>
    [Required, MaxLength(80)]
    public string Name { get; set; } = null!;

    /// <summary>Thẻ locale để định dạng ngày/giờ/số ("vi-VN", "en-US", "ja-JP"). Đây là thứ Lang.locale trả
    /// về cho toLocaleString — sai thẻ thì ngày tháng hiện đúng chữ nhưng sai thứ tự ngày/tháng.</summary>
    [Required, MaxLength(20)]
    public string LocaleTag { get; set; } = null!;

    /// <summary>Thứ tự trong ô chọn, và cũng là thứ tự vòng lặp khi bấm nút chuyển ngôn ngữ.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Ngôn ngữ đi kèm phần mềm (có sẵn file trong wwwroot/locales) — KHÔNG cho xóa, vì bản dịch
    /// gốc sẽ được nạp lại ngay lần khởi động sau và ngôn ngữ đó "sống lại" một cách khó hiểu.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Tắt để tạm ẩn khỏi nút chuyển ngôn ngữ mà không mất bản dịch đã làm — dùng khi đang dịch dở
    /// và chưa muốn người dùng nhìn thấy một giao diện nửa Việt nửa Nhật.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
