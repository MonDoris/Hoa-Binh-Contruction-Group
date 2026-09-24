// File: Domain/Entities/ShopRedemption.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một lượt người học đổi điểm lấy quà ở cửa hàng — vừa là đơn hàng để quản trị viên biết phải
/// trao gì cho ai, vừa là sổ chi tiêu điểm.
///
/// SỐ ĐIỂM KHẢ DỤNG của một người = tổng điểm tích lũy (HBE_USER_POINT_LOG) trừ tổng
/// <see cref="TotalPoint"/> của các lượt đổi CHƯA BỊ HỦY. Cố ý KHÔNG ghi một dòng điểm âm vào nhật ký
/// điểm: tổng nhật ký điểm chính là điểm xếp hạng — trừ vào đó thì đổi một cái cốc là tụt hạng, rơi khỏi
/// bậc đã đạt và tụt trên bảng xếp hạng toàn công ty. Điểm tích lũy đo việc HỌC, còn cửa hàng tiêu vào
/// một quỹ riêng suy ra từ đó.</summary>
public class ShopRedemption
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ShopProductId { get; set; }
    public ShopProduct ShopProduct { get; set; } = null!;

    /// <summary>Tên sản phẩm CHỐT tại thời điểm đổi. Trùng lặp có chủ đích: quản trị viên đổi tên món
    /// ("Áo thun" -> "Áo thun HB 2026") thì lịch sử của người đã đổi vẫn phải đọc ra đúng thứ họ nhận.</summary>
    [Required, MaxLength(200)]
    public string ProductName { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    /// <summary>Giá điểm THỰC TRẢ cho MỘT sản phẩm tại thời điểm đổi — đã trừ khuyến mãi nếu lúc đó có
    /// đợt nào đang chạy. Xem <see cref="ShopProduct.PointCost"/> và <see cref="ShopPromotion"/>.</summary>
    public int UnitPointCost { get; set; }

    /// <summary>Giá NIÊM YẾT của một sản phẩm tại thời điểm đổi, trước khi trừ khuyến mãi. Bằng
    /// <see cref="UnitPointCost"/> khi lượt đổi không rơi vào đợt nào.
    ///
    /// Lưu riêng thay vì tra lại ShopProduct.PointCost lúc cần: giá niêm yết có thể đã bị sửa từ lâu sau
    /// lượt đổi này, và khi đó "đã tiết kiệm bao nhiêu" tính ra một con số chưa từng đúng với ai.</summary>
    public int OriginalPointCost { get; set; }

    /// <summary>Đợt khuyến mãi đã áp cho lượt đổi này, null nếu đổi với giá niêm yết. Để null khi đợt bị
    /// xóa (xem AppDbContext) — lúc đó <see cref="PromotionName"/> vẫn còn để đọc lịch sử.</summary>
    public int? ShopPromotionId { get; set; }
    public ShopPromotion? ShopPromotion { get; set; }

    /// <summary>Tên đợt khuyến mãi CHỐT tại thời điểm đổi, cùng lý do với <see cref="ProductName"/>: đổi
    /// tên đợt hay xóa hẳn đợt cũng không được làm sai lịch sử của người đã đổi.</summary>
    [MaxLength(150)]
    public string? PromotionName { get; set; }

    /// <summary>Tổng điểm đã trừ = <see cref="UnitPointCost"/> × <see cref="Quantity"/>. Lưu sẵn thay vì
    /// nhân lại mỗi lần đọc, vì đây là con số quyết định điểm khả dụng — phép nhân nằm rải rác ở nhiều chỗ
    /// tính là sớm muộn cũng có một chỗ quên.</summary>
    public int TotalPoint { get; set; }

    public ShopRedemptionStatus Status { get; set; } = ShopRedemptionStatus.Pending;

    /// <summary>Ghi chú của quản trị viên khi trao quà hoặc hủy (VD "Đã trao tại phòng Nhân sự 05/09",
    /// "Hết hàng, hoàn điểm"). Người đổi đọc được lời này trong lịch sử của mình.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Lúc quản trị viên chốt trao quà / hủy. Null khi còn đang chờ.</summary>
    public DateTime? HandledAt { get; set; }

    public int? HandledById { get; set; }
    public User? HandledBy { get; set; }

    // ==================== HÀNH VI: CHỐT LƯỢT ĐỔI ====================

    /// <summary>Còn đang chờ trao quà — chỉ lúc này mới chốt được.</summary>
    public bool IsPending => Status == ShopRedemptionStatus.Pending;

    /// <summary>Chốt lượt đổi: Đã trao quà hoặc Đã hủy. Không có đường quay lại trạng thái chờ, và một lượt chỉ chốt
    /// được một lần.
    ///
    /// Hủy thì trả số lượng về kho (cần <see cref="ShopProduct"/> đã được nạp). Điểm KHÔNG cần "hoàn" bằng một thao
    /// tác riêng: điểm khả dụng được tính từ các lượt đổi chưa hủy, nên riêng việc đánh dấu hủy đã trả điểm về.</summary>
    public void Close(ShopRedemptionStatus finalStatus, int handledById, string? note, DateTime now)
    {
        if (finalStatus is not (ShopRedemptionStatus.Delivered or ShopRedemptionStatus.Cancelled))
            throw new ArgumentException($"Chỉ chốt được sang Đã trao hoặc Đã hủy (nhận: {finalStatus}).", nameof(finalStatus));
        if (!IsPending)
            throw new InvalidOperationException($"Lượt đổi đã được chốt ({Status}).");

        Status = finalStatus;
        Note = note?.Trim();
        HandledAt = now;
        HandledById = handledById;

        if (finalStatus == ShopRedemptionStatus.Cancelled)
            ShopProduct.ReturnToStock(Quantity, now);
    }
}
