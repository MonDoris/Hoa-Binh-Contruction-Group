// File: Application/Features/Shop/IShopService.cs

namespace HbELearning.Application.Features.Shop;

/// <summary>Các ca sử dụng của chức năng Shop. Tầng API gọi qua cổng này (xem API/Controllers/ShopService.cs).</summary>
public interface IShopService
{
    /// <summary>Điểm tích lũy / đã tiêu / còn tiêu được của người đang đăng nhập.</summary>
    Task<Result> GetMyBalance();

    /// <summary>Danh sách sản phẩm trong cửa hàng.</summary>
    /// <param name="includeInactive">Kèm cả món đã ngừng bán — chỉ quản trị viên mới xem được, vì món đã
    /// tắt không đổi được nữa thì bày ra cho người học chỉ tạo một cú bấm dẫn tới lời từ chối.</param>
    Task<Result> GetProductList(string? keyword = null, string? category = null, bool includeInactive = false);

    Task<Result> CreateProduct(ShopProductRequest request);

    Task<Result> UpdateProduct(int id, ShopProductRequest request);

    Task<Result> DeleteProduct(int id);

    /// <summary>Người học đổi điểm lấy quà: trừ điểm khả dụng và trừ kho ngay tại đây (xem
    /// <see cref="ShopRedemptionStatus"/> về lý do không đợi tới lúc trao quà).</summary>
    Task<Result> Redeem(int id, RedeemRequest request);

    /// <summary>Lịch sử đổi quà của người đang đăng nhập.</summary>
    Task<Result> GetMyRedemptionList();

    /// <summary>Ai đã đổi MỘT sản phẩm cụ thể — mở từ nút "Người đã đổi" trên thẻ sản phẩm.</summary>
    Task<Result> GetProductRedemptionList(int id);

    /// <summary>Toàn bộ lượt đổi của mọi người, lọc theo trạng thái và theo tên người / tên sản phẩm.</summary>
    Task<Result> GetRedemptionList(ShopRedemptionStatus? status = null, string? keyword = null);

    /// <summary>Chốt một lượt đổi: đã trao quà, hoặc hủy và hoàn lại điểm + số lượng.</summary>
    Task<Result> UpdateRedemptionStatus(int id, UpdateRedemptionStatusRequest request);

    /// <summary>Danh sách đợt khuyến mãi.
    ///
    /// Người học thấy các đợt ĐANG CHẠY để biết vì sao món rẻ đi và đợt còn mấy ngày; quản trị viên truyền
    /// <paramref name="includeAll"/> để thấy cả đợt sắp tới, đã kết thúc và đang tắt.</summary>
    Task<Result> GetPromotionList(bool includeAll = false);

    Task<Result> CreatePromotion(ShopPromotionRequest request);

    Task<Result> UpdatePromotion(int id, ShopPromotionRequest request);

    Task<Result> DeletePromotion(int id);
}
