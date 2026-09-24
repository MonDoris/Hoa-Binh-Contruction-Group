// File: Domain/Entities/ShopProduct.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một phần quà trong CỬA HÀNG ĐIỂM THƯỞNG — thứ người học đổi được bằng điểm tích lũy (viên
/// kim cương xanh trên thanh trên).
///
/// Khác với phần thưởng của bậc hạng (<see cref="RankTier.RewardDescription"/> + kho mã voucher): phần
/// thưởng bậc hạng được cấp TỰ ĐỘNG một lần khi chạm mốc điểm và người học không chọn gì cả. Cửa hàng
/// ngược lại — người học tự quyết đổi món nào, lúc nào, và đổi bao nhiêu lần miễn còn điểm và còn hàng.
/// Hai cơ chế bổ sung cho nhau nên cố ý để riêng, không gộp vào bảng bậc hạng.</summary>
public class ShopProduct
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Nhóm phân loại tự đặt (VD "Đồ dùng văn phòng", "Voucher", "Đồ điện tử") — để lọc khi cửa
    /// hàng đã nhiều món.</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Ảnh sản phẩm trong wwwroot (VD /uploads/shop/202609/xxx.jpg). Null thì thẻ hàng hiện icon
    /// quà mặc định — một cửa hàng toàn chữ thì gần như không ai buồn đổi.</summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Số điểm phải trả cho MỘT sản phẩm. Đổi giá ở đây KHÔNG ảnh hưởng các lượt đổi đã ghi:
    /// mỗi lượt đổi đã chốt giá tại thời điểm đó (<see cref="ShopRedemption.UnitPointCost"/>).</summary>
    public int PointCost { get; set; }

    /// <summary>Số lượng còn trong kho. 0 = hết hàng: người học vẫn thấy sản phẩm (để biết công ty có món
    /// này) nhưng không bấm đổi được. Trừ ngay lúc đổi và cộng trả lại khi lượt đổi bị hủy.
    ///
    /// [ConcurrencyCheck]: câu UPDATE sinh ra sẽ kèm điều kiện "STOCK vẫn đúng bằng giá trị vừa đọc". Không
    /// có nó thì hai người cùng bấm đổi món cuối cùng sẽ cùng đọc thấy Stock = 1, cùng ghi Stock = 0, và cả
    /// hai đều đổi được một món chỉ còn một cái. Có nó thì người ghi sau nhận DbUpdateConcurrencyException
    /// và ShopService trả về lời mời thử lại.</summary>
    [ConcurrencyCheck]
    public int Stock { get; set; }

    /// <summary>Còn bày bán hay không. Ngừng bán một món thì tắt cờ này chứ đừng xóa — xóa là mất luôn tên
    /// món trong lịch sử của những người đã đổi nó.</summary>
    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<ShopRedemption> RedemptionList { get; set; } = new List<ShopRedemption>();

    // ==================== HÀNH VI: KHO ====================

    /// <summary>Trừ kho khi có người đổi. Kho không bao giờ âm — nơi gọi phải kiểm tra còn đủ hàng và báo lý do cho
    /// người dùng trước; tới được đây mà thiếu hàng nghĩa là mã gọi sai, nên ném lỗi thay vì lặng lẽ ghi số âm.</summary>
    public void TakeFromStock(int quantity, DateTime now)
    {
        if (quantity <= 0 || Stock < quantity)
            throw new InvalidOperationException($"Không trừ được {quantity} khỏi kho còn {Stock}.");
        Stock -= quantity;
        UpdatedAt = now;
    }

    /// <summary>Trả số lượng về kho (lượt đổi bị hủy).</summary>
    public void ReturnToStock(int quantity, DateTime now)
    {
        Stock += quantity;
        UpdatedAt = now;
    }
}
