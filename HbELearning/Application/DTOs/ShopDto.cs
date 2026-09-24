// File: Application/DTOs/ShopDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Ví điểm của người đang đăng nhập, nhìn từ phía cửa hàng.</summary>
/// <param name="TotalPoint">Điểm tích lũy — đúng con số trên viên kim cương ở thanh trên và dùng cho xếp hạng.
/// KHÔNG bao giờ giảm khi đổi quà.</param>
/// <param name="UsedPoint">Tổng điểm đã tiêu ở cửa hàng, không tính các lượt đã bị hủy (đã được hoàn).</param>
/// <param name="AvailablePoint">Còn tiêu được bao nhiêu = <paramref name="TotalPoint"/> − <paramref name="UsedPoint"/>.</param>
/// <param name="PendingCount">Số lượt đang chờ được trao quà — để nhắc người học ra nhận.</param>
public record ShopBalanceDto(int TotalPoint, int UsedPoint, int AvailablePoint, int PendingCount);

/// <summary>Một món trong cửa hàng.</summary>
/// <param name="RedeemedCount">Tổng số sản phẩm đã đổi đi (không tính lượt đã hủy) — quản trị viên nhìn ra
/// món nào được ưa chuộng, và đây cũng là số dòng sẽ hiện ở màn hình "ai đã đổi món này".</param>
/// <param name="PointCost">Giá NIÊM YẾT, đúng con số trong bảng sản phẩm. Khuyến mãi không sửa nó — xem
/// <see cref="ShopPromotion"/>.</param>
/// <param name="EffectivePointCost">Giá THỰC TRẢ hôm nay, đã trừ đợt khuyến mãi tốt nhất đang chạy. Bằng
/// <paramref name="PointCost"/> khi không có đợt nào phủ món này. Giao diện gạch ngang giá niêm yết khi hai
/// con số khác nhau.</param>
/// <param name="CanAfford">Người đang đăng nhập có đủ điểm khả dụng cho MỘT sản phẩm này không, tính theo
/// GIÁ THỰC TRẢ. Tính ở server để giao diện không phải tự làm lại phép trừ và lệch với kết quả lúc bấm đổi.</param>
/// <param name="PromotionName">Tên đợt đang giảm giá món này, null nếu đang bán giá niêm yết.</param>
/// <param name="DiscountLabel">Nhãn ngắn hiện trên thẻ hàng, VD "-30%" hoặc "-50 điểm".</param>
public record ShopProductDto(
    int Id, string Name, string? Description, string? Category, string? ImageUrl,
    int PointCost, int EffectivePointCost, int Stock, bool IsActive, int RedeemedCount, bool CanAfford,
    string? CreatedByName, DateTime CreatedAt,
    int? PromotionId = null, string? PromotionName = null, string? PromotionColorHex = null,
    string? PromotionIcon = null, string? DiscountLabel = null, DateTime? PromotionEndAt = null);

public class ShopProductRequest
{
    [Required(ErrorMessage = "Tên sản phẩm là bắt buộc"), MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Giá điểm phải lớn hơn 0")]
    public int PointCost { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số lượng không được âm")]
    public int Stock { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Yêu cầu đổi quà của người học.</summary>
public class RedeemRequest
{
    /// <summary>Đổi nhiều cái cùng lúc trong một lượt. Chặn trần ở 20 để một ô nhập bị gõ nhầm (hoặc một
    /// request tự dựng) không quét sạch kho trong một cú bấm.</summary>
    [Range(1, 20, ErrorMessage = "Số lượng mỗi lượt đổi từ 1 đến 20")]
    public int Quantity { get; set; } = 1;
}

/// <summary>Một lượt đổi quà. Dùng chung cho ba màn hình: lịch sử của tôi, danh sách người đã đổi một món,
/// và hàng chờ trao quà của quản trị viên — nên mang đủ cả thông tin người đổi lẫn thông tin sản phẩm.</summary>
/// <param name="UnitPointCost">Giá thực trả cho một sản phẩm, đã trừ khuyến mãi.</param>
/// <param name="OriginalPointCost">Giá niêm yết tại thời điểm đổi. Khác <paramref name="UnitPointCost"/>
/// nghĩa là lượt đổi này rơi vào một đợt khuyến mãi.</param>
/// <param name="SavedPoint">Số điểm tiết kiệm được nhờ đợt đó, đã nhân số lượng. 0 khi đổi giá niêm yết.</param>
public record ShopRedemptionDto(
    int Id, int UserId, string UserName, string? UserEmployeeCode, string? UserDepartmentName, string? UserAvatarUrl,
    int ShopProductId, string ProductName, string? ProductImageUrl,
    int Quantity, int UnitPointCost, int OriginalPointCost, int SavedPoint, string? PromotionName, int TotalPoint,
    string Status, string StatusLabel, string? Note,
    DateTime CreatedAt, DateTime? HandledAt, string? HandledByName);

/// <summary>Chốt một lượt đổi: trao quà hoặc hủy (hủy thì hoàn điểm và trả số lượng về kho).</summary>
public class UpdateRedemptionStatusRequest
{
    /// <summary>Chỉ nhận <see cref="ShopRedemptionStatus.Delivered"/> hoặc
    /// <see cref="ShopRedemptionStatus.Cancelled"/> — không có đường quay lại trạng thái chờ.</summary>
    public ShopRedemptionStatus Status { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}


// ==================== KHUYẾN MÃI ====================

/// <summary>Một đợt khuyến mãi của cửa hàng.</summary>
/// <param name="Status">"Scheduled" (chưa tới ngày) / "Running" (đang chạy) / "Ended" (đã qua) /
/// "Paused" (bị tắt thủ công). Tính ở server từ đồng hồ máy chủ — để giao diện tự so ngày thì máy người
/// dùng lệch giờ là nhãn "đang chạy" nói một đằng, giá tính một nẻo.</param>
/// <param name="ProductCount">Số sản phẩm đang thực sự được đợt này giảm giá — với phạm vi "toàn cửa hàng"
/// và "theo nhóm" thì đây là con số đếm sống, không phải số dòng admin đã tick.</param>
public record ShopPromotionDto(
    int Id, string Name, string? Description, string ColorHex, string Icon,
    string DiscountType, int DiscountValue, string DiscountLabel,
    string ScopeType, string ScopeLabel, string? ScopeCategory, List<int> ProductIdList,
    DateTime StartAt, DateTime EndAt, bool IsActive,
    string Status, string StatusLabel, int ProductCount, int RedemptionCount, int SavedPointTotal,
    string? CreatedByName, DateTime CreatedAt);

public class ShopPromotionRequest
{
    [Required(ErrorMessage = "Tên chương trình là bắt buộc"), MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(20)]
    public string ColorHex { get; set; } = "#dc2626";

    [Required, MaxLength(40)]
    public string Icon { get; set; } = "confetti";

    public ShopDiscountType DiscountType { get; set; }

    /// <summary>Trần 100 là trần của kiểu phần trăm; kiểu trừ điểm cố định còn được kiểm riêng trong
    /// ShopService vì mức hợp lệ của nó phụ thuộc giá từng món.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Mức giảm phải lớn hơn 0")]
    public int DiscountValue { get; set; }

    public ShopPromotionScope ScopeType { get; set; }

    [MaxLength(100)]
    public string? ScopeCategory { get; set; }

    /// <summary>Chỉ dùng khi <see cref="ScopeType"/> = SelectedProduct.</summary>
    public List<int> ProductIdList { get; set; } = new();

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    public bool IsActive { get; set; } = true;
}
