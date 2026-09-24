// File: Domain/Entities/ShopPromotion.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một đợt khuyến mãi của cửa hàng điểm thưởng, do quản trị viên tạo cho một sự kiện công ty
/// (sinh nhật công ty, Tết, tháng an toàn lao động...): trong khoảng thời gian đã đặt, những sản phẩm
/// thuộc phạm vi của đợt được đổi với giá điểm rẻ hơn.
///
/// KHÔNG sửa thẳng <see cref="ShopProduct.PointCost"/> khi tới đợt rồi sửa trả lại lúc hết đợt. Cách đó
/// hỏng ở ba chỗ: giá gốc không còn ở đâu để hiện "giá cũ 500 → nay 350"; hết đợt mà quên sửa lại thì công
/// ty bán rẻ mãi mãi; và không ai trả lời được câu "đợt Tết vừa rồi giảm bao nhiêu" sau khi số đã bị ghi
/// đè. Giá gốc nằm yên ở sản phẩm, đợt khuyến mãi là một lớp phủ có ngày bắt đầu/kết thúc.
///
/// HAI ĐỢT CÙNG PHỦ MỘT MÓN: lấy đợt cho ra giá THẤP NHẤT (bằng nhau thì lấy đợt bắt đầu muộn hơn), và
/// KHÔNG cộng dồn. Cộng dồn thì hai đợt "giảm 60%" chồng nhau là món gần như miễn phí — một sự kiện quên
/// tắt đủ làm rỗng kho quà. Xem ShopService.ApplyBestPromotion.</summary>
public class ShopPromotion
{
    public int Id { get; set; }

    /// <summary>Tên đợt, hiện thẳng trên nhãn của thẻ hàng (VD "Sinh nhật công ty 2026") — đặt sao cho
    /// người học nhìn nhãn là biết đang được giảm vì dịp gì.</summary>
    [Required, MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Màu của nhãn khuyến mãi trên thẻ hàng và của biểu ngữ đầu trang cửa hàng. Mỗi sự kiện một
    /// màu để hai đợt chạy song song còn phân biệt được bằng mắt.</summary>
    [Required, MaxLength(20)]
    public string ColorHex { get; set; } = "#dc2626";

    /// <summary>Tên biểu tượng Phosphor không có tiền tố (VD "confetti", "gift") — cùng quy ước với
    /// <see cref="RankTier.Icon"/>.</summary>
    [Required, MaxLength(40)]
    public string Icon { get; set; } = "confetti";

    public ShopDiscountType DiscountType { get; set; }

    /// <summary>Phần trăm giảm (1..100) hoặc số điểm trừ thẳng, tùy <see cref="DiscountType"/>.
    /// Giá sau khi giảm luôn được kéo lên tối thiểu 1 điểm: món 0 điểm không còn là "đổi bằng điểm" nữa,
    /// và ai cũng lấy được không giới hạn cho tới khi hết kho.</summary>
    public int DiscountValue { get; set; }

    public ShopPromotionScope ScopeType { get; set; }

    /// <summary>Nhóm sản phẩm được giảm, chỉ có nghĩa khi <see cref="ScopeType"/> là
    /// <see cref="ShopPromotionScope.Category"/>. Khớp <see cref="ShopProduct.Category"/> theo đúng chuỗi.</summary>
    [MaxLength(100)]
    public string? ScopeCategory { get; set; }

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    /// <summary>Tắt tạm cả đợt mà không phải xóa hay sửa ngày — dùng khi sự kiện bị hoãn. Đợt đang tắt
    /// không giảm giá gì cả, kể cả khi hôm nay nằm trong khoảng thời gian của nó.</summary>
    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<ShopPromotionItem> ItemList { get; set; } = new List<ShopPromotionItem>();
}
