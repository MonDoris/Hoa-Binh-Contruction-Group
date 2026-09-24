// File: Domain/Entities/ShopPromotionItem.cs
namespace HbELearning.Domain.Entities;

/// <summary>Một sản phẩm được admin tick vào đợt khuyến mãi — chỉ dùng khi đợt đó có
/// <see cref="ShopPromotion.ScopeType"/> = <see cref="Helpers.ShopPromotionScope.SelectedProduct"/>.
///
/// Đợt phủ toàn cửa hàng hoặc phủ theo nhóm KHÔNG sinh dòng nào ở đây: chép danh sách sản phẩm ra bảng
/// này lúc tạo đợt thì món thêm mới giữa đợt sẽ nằm ngoài, đúng cái mà hai phạm vi kia sinh ra để tránh.</summary>
public class ShopPromotionItem
{
    public int Id { get; set; }

    public int ShopPromotionId { get; set; }
    public ShopPromotion ShopPromotion { get; set; } = null!;

    public int ShopProductId { get; set; }
    public ShopProduct ShopProduct { get; set; } = null!;
}
