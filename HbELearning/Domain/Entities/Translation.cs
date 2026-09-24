// File: Domain/Entities/Translation.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một dòng trong bảng dịch giao diện: (khóa, ngôn ngữ) -> câu chữ hiển thị.
///
/// Vì sao đưa bản dịch vào CSDL trong khi đã có wwwroot/locales/*.json: mỗi ngành có bộ từ riêng mà bản
/// dịch xuất xưởng không thể đoán trước ("giàn giáo", "biện pháp thi công", "phiếu nghiệm thu"...). Trước
/// đây sửa một chữ phải sửa file JSON rồi triển khai lại — nghiệp vụ không tự làm được. Có bảng này thì
/// màn hình "Dịch ngôn ngữ" sửa thẳng trên giao diện, có hiệu lực ngay sau khi tải lại trang.
///
/// FILE JSON VẪN LÀ BẢN GỐC, không bị bỏ đi: mỗi lần khởi động, TranslationSeeder nạp chúng vào đây với
/// <see cref="IsSystem"/> = true và ghi lại nguyên văn vào <see cref="DefaultText"/>. Nhờ vậy hệ thống
/// luôn biết "bản xuất xưởng của câu này là gì", nên vừa hiện được nhãn "đã sửa", vừa khôi phục lại được,
/// vừa nhận được bản dịch mới khi nâng cấp phần mềm mà không đè lên chữ công ty đã sửa.</summary>
public class Translation
{
    public int Id { get; set; }

    /// <summary>Khóa i18n, dạng "trang.thanhphan" (VD "menu.dashboard") — đúng khóa mà t() ở
    /// wwwroot/js/language.js tra cứu. Đặt tên cột là RESOURCE_KEY chứ không phải KEY vì KEY là từ khóa
    /// dành riêng của Oracle.</summary>
    [Required, MaxLength(150)]
    public string ResourceKey { get; set; } = null!;

    /// <summary>Mã ngôn ngữ ("vi", "en") — đúng bằng tên file trong wwwroot/locales.</summary>
    [Required, MaxLength(10)]
    public string Lang { get; set; } = null!;

    /// <summary>Câu chữ ĐANG dùng để hiển thị. Đây là thứ duy nhất người học nhìn thấy.</summary>
    [Required, MaxLength(2000)]
    public string TranslatedText { get; set; } = null!;

    /// <summary>Câu chữ xuất xưởng lấy từ file JSON. Null với khóa do công ty tự thêm (không có bản gốc
    /// nào để so). Khác <see cref="TranslatedText"/> nghĩa là dòng này đã được sửa lại.</summary>
    [MaxLength(2000)]
    public string? DefaultText { get; set; }

    /// <summary>true = khóa đi kèm phần mềm (nạp từ wwwroot/locales). false = khóa do công ty tự thêm.
    /// Khóa hệ thống KHÔNG cho xóa: mã nguồn đang gọi t() với khóa đó, xóa đi thì màn hình rơi về chuỗi
    /// dự phòng trong HTML mà không ai hiểu vì sao.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Ghi chú của người sửa — VD "theo thuật ngữ hồ sơ thầu của phòng Kỹ thuật". Giúp người sau
    /// biết vì sao câu chữ ở đây khác bản gốc, thay vì tưởng là dịch sai rồi sửa ngược lại.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public int? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
