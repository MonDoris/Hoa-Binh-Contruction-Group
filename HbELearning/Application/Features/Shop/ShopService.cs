// File: Application/Features/Shop/ShopService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Shop;

/// <summary>Cửa hàng điểm thưởng: người học tiêu điểm tích lũy để đổi quà, quản trị viên quản kho hàng và
/// trao quà.
///
/// Quy ước điểm của cả màn hình này nằm ở <see cref="ShopRedemption"/>: điểm tích lũy
/// (HBE_USER_POINT_LOG) KHÔNG bị trừ khi đổi quà — nó là điểm xếp hạng, đo việc học. Cửa hàng tiêu vào
/// "điểm khả dụng" suy ra bằng phép trừ, xem <see cref="GetBalanceAsync"/>.</summary>
public class ShopService : IShopService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ShopService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetMyBalance()
    {
        var (total, used, available) = await GetBalanceAsync(_currentUser.UserId);
        var pendingCount = await _db.ShopRedemption.CountAsync(
            x => x.UserId == _currentUser.UserId && x.Status == ShopRedemptionStatus.Pending);

        return Result.Success(new ShopBalanceDto(total, used, available, pendingCount));
    }

    public async Task<Result> GetProductList(string? keyword = null, string? category = null, bool includeInactive = false)
    {
        var canManage = CanManageShop;
        var query = _db.ShopProduct.AsNoTracking();

        if (!includeInactive || !canManage) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(x => x.Name.Contains(k)
                || (x.Description != null && x.Description.Contains(k))
                || (x.Category != null && x.Category.Contains(k)));
        }

        var (_, _, available) = await GetBalanceAsync(_currentUser.UserId);
        var promotionList = await GetRunningPromotionListAsync();

        var rowList = await query
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.Category, x.ImageUrl,
                x.PointCost, x.Stock, x.IsActive, x.CreatedAt,
                RedeemedCount = x.RedemptionList
                    .Where(r => r.Status != ShopRedemptionStatus.Cancelled).Sum(r => (int?)r.Quantity) ?? 0,
                CreatedByName = x.CreatedBy != null ? x.CreatedBy.FullName : null
            })
            .ToListAsync();

        // Giá khuyến mãi tính TRONG BỘ NHỚ, không dịch sang SQL: quy tắc "đợt nào cho giá thấp nhất" phải
        // giống hệt ở đây và ở lúc bấm đổi, mà viết hai lần bằng hai thứ ngôn ngữ là hai lần để lệch nhau.
        var list = rowList
            .Select(x =>
            {
                var (cost, promotion) = ApplyBestPromotion(x.Id, x.Category, x.PointCost, promotionList);
                return new ShopProductDto(
                    x.Id, x.Name, x.Description, x.Category, x.ImageUrl,
                    x.PointCost, cost, x.Stock, x.IsActive, x.RedeemedCount,
                    available >= cost, x.CreatedByName, x.CreatedAt,
                    promotion?.Id, promotion?.Name, promotion?.ColorHex, promotion?.Icon,
                    promotion is null ? null : DiscountLabel(promotion.DiscountType, promotion.DiscountValue),
                    promotion?.EndAt);
            })
            // Còn hàng lên trước, rồi tới món rẻ điểm nhất TÍNH THEO GIÁ ĐANG BÁN: sắp theo giá niêm yết thì
            // đúng những món vừa được giảm sâu nhất lại bị đẩy xuống cuối, trái hẳn ý của một đợt khuyến mãi.
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Stock > 0)
            .ThenBy(x => x.EffectivePointCost)
            .ToList();

        return Result.Success(list);
    }

    public async Task<Result> CreateProduct(ShopProductRequest request)
    {
        var name = request.Name.Trim();
        if (await _db.ShopProduct.AnyAsync(x => x.Name == name))
            return Result.Invalid(new MessageResponse("Cửa hàng đã có sản phẩm trùng tên này."));

        var entity = new ShopProduct
        {
            Name = name,
            Description = request.Description?.Trim(),
            Category = request.Category?.Trim(),
            ImageUrl = request.ImageUrl?.Trim(),
            PointCost = request.PointCost,
            Stock = request.Stock,
            IsActive = request.IsActive,
            CreatedById = _currentUser.UserId,
        };
        _db.ShopProduct.Add(entity);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã thêm sản phẩm vào cửa hàng.", entity.Id));
    }

    public async Task<Result> UpdateProduct(int id, ShopProductRequest request)
    {
        var entity = await _db.ShopProduct.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy sản phẩm."));

        var name = request.Name.Trim();
        if (await _db.ShopProduct.AnyAsync(x => x.Name == name && x.Id != id))
            return Result.Invalid(new MessageResponse("Cửa hàng đã có sản phẩm trùng tên này."));

        entity.Name = name;
        entity.Description = request.Description?.Trim();
        entity.Category = request.Category?.Trim();
        entity.ImageUrl = request.ImageUrl?.Trim();
        entity.PointCost = request.PointCost;
        entity.Stock = request.Stock;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.Now;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Số lượng vừa đổi vì có người bấm đổi quà giữa lúc form này đang mở — xem ShopProduct.Stock.
            return Result.Conflict(new MessageResponse(
                "Có người vừa đổi sản phẩm này nên số lượng trong kho đã thay đổi. Hãy mở lại và nhập số lượng theo mức mới nhất."));
        }

        return Result.Success(new MessageResponse("Đã cập nhật sản phẩm."));
    }

    public async Task<Result> DeleteProduct(int id)
    {
        var entity = await _db.ShopProduct.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy sản phẩm."));

        // Đã có người đổi thì không xóa: lượt đổi là sổ chi tiêu điểm của họ, mất dòng sản phẩm là lịch sử
        // không còn đọc ra được nữa. Ngừng bán bằng cách bỏ tick "Còn bày bán".
        if (await _db.ShopRedemption.AnyAsync(x => x.ShopProductId == id))
            return Result.Invalid(new MessageResponse(
                "Đã có người đổi sản phẩm này nên không xóa được — lịch sử đổi quà của họ sẽ mất theo. "
                + "Hãy bỏ tick \"Còn bày bán\" để ngừng bán mà vẫn giữ lịch sử."));

        // Khóa ngoại từ HBE_SHOP_PROMOTION_ITEM tới sản phẩm để NoAction (xem AppDbContext), nên phải tự dọn
        // ở đây — bỏ qua thì cú xóa chết ở tầng CSDL với một thông báo tiếng Anh không nói được gì.
        var itemList = await _db.ShopPromotionItem.Where(x => x.ShopProductId == id).ToListAsync();
        _db.ShopPromotionItem.RemoveRange(itemList);

        _db.ShopProduct.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa sản phẩm khỏi cửa hàng."));
    }

    public async Task<Result> Redeem(int id, RedeemRequest request)
    {
        var product = await _db.ShopProduct.FirstOrDefaultAsync(x => x.Id == id);
        if (product is null) return Result.NotFound(new MessageResponse("Không tìm thấy sản phẩm."));
        if (!product.IsActive) return Result.Invalid(new MessageResponse("Sản phẩm này đã ngừng bày bán."));

        var quantity = request.Quantity;
        if (product.Stock < quantity)
            return Result.Invalid(new MessageResponse(product.Stock == 0
                ? "Sản phẩm đã hết hàng."
                : $"Trong kho chỉ còn {product.Stock} sản phẩm."));

        // Giá tính LẠI ở server tại đúng thời điểm này, không nhận giá do trình duyệt gửi lên: thẻ hàng có
        // thể đã mở từ trước lúc đợt khuyến mãi kết thúc, và tin vào con số đó là bán rẻ ngoài đợt.
        var promotionList = await GetRunningPromotionListAsync();
        var (unitCost, promotion) = ApplyBestPromotion(product.Id, product.Category, product.PointCost, promotionList);

        var totalPoint = unitCost * quantity;
        var (_, _, available) = await GetBalanceAsync(_currentUser.UserId);
        if (available < totalPoint)
            return Result.Invalid(new MessageResponse(
                $"Bạn cần {totalPoint} điểm cho lượt đổi này nhưng chỉ còn {available} điểm khả dụng."));

        product.TakeFromStock(quantity, DateTime.Now);

        var redemption = new ShopRedemption
        {
            UserId = _currentUser.UserId,
            ShopProductId = product.Id,
            ProductName = product.Name,
            Quantity = quantity,
            UnitPointCost = unitCost,
            OriginalPointCost = product.PointCost,
            ShopPromotionId = promotion?.Id,
            PromotionName = promotion?.Name,
            TotalPoint = totalPoint,
            Status = ShopRedemptionStatus.Pending,
        };
        _db.ShopRedemption.Add(redemption);

        // Biên nhận cho chính người đổi: điểm đã bị trừ ngay lúc này, nên phải có một dấu vết họ đọc lại được
        // ngoài màn hình cửa hàng — nhất là khi việc trao quà diễn ra ở đời thực vài ngày sau.
        _db.Notification.Add(new Notification
        {
            UserId = _currentUser.UserId,
            Type = NotificationType.ShopRedemption,
            Title = $"🎁 Đã đổi thành công: {product.Name}",
            Message = $"Bạn vừa dùng {totalPoint} điểm để đổi {quantity} × {product.Name}."
                + (promotion is null ? "" : $" Tiết kiệm {(product.PointCost - unitCost) * quantity} điểm nhờ \"{promotion.Name}\".")
                + " Hãy chờ quản trị viên liên hệ trao quà.",
        });

        try
        {
            // Trừ kho và ghi lượt đổi nằm trong CÙNG một SaveChanges nên hoặc cả hai cùng vào, hoặc không có
            // gì vào. Riêng trường hợp hai người cùng bấm đổi món cuối thì người ghi sau rơi vào đây — xem
            // [ConcurrencyCheck] trên ShopProduct.Stock.
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict(new MessageResponse(
                "Có người vừa đổi sản phẩm này trước bạn một nhịp. Hãy tải lại cửa hàng và thử lại — điểm của bạn chưa bị trừ."));
        }

        var savedNote = promotion is null ? ""
            : $" (tiết kiệm {(product.PointCost - unitCost) * quantity} điểm nhờ \"{promotion.Name}\")";
        return Result.Success(new MessageResponse(
            $"Đã đổi {quantity} × {product.Name}. Đã trừ {totalPoint} điểm{savedNote}, đang chờ trao quà.", redemption.Id));
    }

    public async Task<Result> GetMyRedemptionList()
        => Result.Success(await ToRedemptionListAsync(_db.ShopRedemption.Where(x => x.UserId == _currentUser.UserId)));

    public async Task<Result> GetProductRedemptionList(int id)
        => Result.Success(await ToRedemptionListAsync(_db.ShopRedemption.Where(x => x.ShopProductId == id)));

    public async Task<Result> GetRedemptionList(ShopRedemptionStatus? status = null, string? keyword = null)
    {
        var query = _db.ShopRedemption.AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(x => x.ProductName.Contains(k)
                || x.User.FullName.Contains(k)
                || x.User.EmployeeCode.Contains(k));
        }

        return Result.Success(await ToRedemptionListAsync(query));
    }

    public async Task<Result> UpdateRedemptionStatus(int id, UpdateRedemptionStatusRequest request)
    {
        if (request.Status is not (ShopRedemptionStatus.Delivered or ShopRedemptionStatus.Cancelled))
            return Result.Invalid(new MessageResponse("Chỉ chuyển được sang trạng thái Đã trao hoặc Đã hủy."));

        var entity = await _db.ShopRedemption.Include(x => x.ShopProduct).FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy lượt đổi."));
        if (!entity.IsPending)
            return Result.Invalid(new MessageResponse(
                $"Lượt đổi này đã được chốt ({StatusLabel(entity.Status)}) nên không đổi trạng thái được nữa."));

        entity.Close(request.Status, _currentUser.UserId, request.Note, DateTime.Now);
        var isCancelled = request.Status == ShopRedemptionStatus.Cancelled;

        _db.Notification.Add(new Notification
        {
            UserId = entity.UserId,
            Type = NotificationType.ShopRedemption,
            Title = isCancelled ? $"↩️ Đã hoàn điểm: {entity.ProductName}" : $"🎁 Đã trao quà: {entity.ProductName}",
            Message = isCancelled
                ? $"Lượt đổi {entity.Quantity} × {entity.ProductName} đã bị hủy, {entity.TotalPoint} điểm đã được hoàn lại."
                    + (string.IsNullOrWhiteSpace(entity.Note) ? "" : $" Lý do: {entity.Note}")
                : $"Bạn đã nhận {entity.Quantity} × {entity.ProductName}."
                    + (string.IsNullOrWhiteSpace(entity.Note) ? "" : $" Ghi chú: {entity.Note}"),
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict(new MessageResponse(
                "Số lượng trong kho vừa thay đổi do có người đổi quà cùng lúc. Hãy tải lại danh sách và thử lại."));
        }

        return Result.Success(new MessageResponse(isCancelled
            ? $"Đã hủy lượt đổi và hoàn {entity.TotalPoint} điểm."
            : "Đã đánh dấu trao quà."));
    }

    public async Task<Result> GetPromotionList(bool includeAll = false)
    {
        var now = DateTime.Now;
        var query = _db.ShopPromotion.AsNoTracking();
        if (!includeAll || !CanManageShop)
            query = query.Where(x => x.IsActive && x.StartAt <= now && x.EndAt >= now);

        var rowList = await query
            .OrderByDescending(x => x.StartAt)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.ColorHex, x.Icon,
                x.DiscountType, x.DiscountValue, x.ScopeType, x.ScopeCategory,
                x.StartAt, x.EndAt, x.IsActive, x.CreatedAt,
                CreatedByName = x.CreatedBy != null ? x.CreatedBy.FullName : null,
                ProductIdList = x.ItemList.Select(i => i.ShopProductId).ToList()
            })
            .ToListAsync();

        if (rowList.Count == 0) return Result.Success(new List<ShopPromotionDto>());

        // Số lượt đổi và tổng điểm đã giảm của từng đợt: đếm trên bảng lượt đổi theo ShopPromotionId chứ
        // không đi qua ItemList — đợt phủ toàn cửa hàng không có dòng ItemList nào mà vẫn phát sinh lượt đổi.
        var idList = rowList.Select(x => x.Id).ToList();
        var statMap = (await _db.ShopRedemption.AsNoTracking()
                .Where(x => x.ShopPromotionId != null && idList.Contains(x.ShopPromotionId.Value)
                            && x.Status != ShopRedemptionStatus.Cancelled)
                .GroupBy(x => x.ShopPromotionId!.Value)
                .Select(g => new
                {
                    PromotionId = g.Key,
                    Count = g.Count(),
                    Saved = g.Sum(x => (x.OriginalPointCost - x.UnitPointCost) * x.Quantity)
                })
                .ToListAsync())
            .ToDictionary(x => x.PromotionId);

        // Số sản phẩm mỗi đợt đang phủ. Đếm sống chứ không lấy ProductIdList.Count: đợt "toàn cửa hàng" và
        // đợt "theo nhóm" không tick món nào mà vẫn giảm giá cả loạt.
        var productList = await _db.ShopProduct.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Category })
            .ToListAsync();

        var result = rowList.Select(x =>
        {
            var stat = statMap.GetValueOrDefault(x.Id);
            var productIdSet = x.ProductIdList.ToHashSet();
            var productCount = productList.Count(p => x.ScopeType switch
            {
                ShopPromotionScope.AllProduct => true,
                ShopPromotionScope.Category => p.Category == x.ScopeCategory,
                _ => productIdSet.Contains(p.Id)
            });

            var status = PromotionStatus(x.IsActive, x.StartAt, x.EndAt, now);
            return new ShopPromotionDto(
                x.Id, x.Name, x.Description, x.ColorHex, x.Icon,
                x.DiscountType.ToString(), x.DiscountValue, DiscountLabel(x.DiscountType, x.DiscountValue),
                x.ScopeType.ToString(), ScopeLabel(x.ScopeType, x.ScopeCategory), x.ScopeCategory, x.ProductIdList,
                x.StartAt, x.EndAt, x.IsActive,
                status, PromotionStatusLabel(status), productCount,
                stat?.Count ?? 0, stat?.Saved ?? 0,
                x.CreatedByName, x.CreatedAt);
        }).ToList();

        return Result.Success(result);
    }

    public async Task<Result> CreatePromotion(ShopPromotionRequest request)
    {
        var error = await ValidatePromotionAsync(request, null);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var entity = new ShopPromotion
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ColorHex = request.ColorHex.Trim(),
            Icon = request.Icon.Trim(),
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            ScopeType = request.ScopeType,
            ScopeCategory = request.ScopeType == ShopPromotionScope.Category ? request.ScopeCategory?.Trim() : null,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            IsActive = request.IsActive,
            CreatedById = _currentUser.UserId,
        };
        _db.ShopPromotion.Add(entity);
        // Lưu trước để có Id, rồi mới ghi danh sách món được tick trỏ vào nó.
        await _db.SaveChangesAsync();

        await ReplacePromotionItemListAsync(entity.Id, request);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã tạo chương trình khuyến mãi.", entity.Id));
    }

    public async Task<Result> UpdatePromotion(int id, ShopPromotionRequest request)
    {
        var entity = await _db.ShopPromotion.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy chương trình khuyến mãi."));

        var error = await ValidatePromotionAsync(request, id);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.ColorHex = request.ColorHex.Trim();
        entity.Icon = request.Icon.Trim();
        entity.DiscountType = request.DiscountType;
        entity.DiscountValue = request.DiscountValue;
        entity.ScopeType = request.ScopeType;
        entity.ScopeCategory = request.ScopeType == ShopPromotionScope.Category ? request.ScopeCategory?.Trim() : null;
        entity.StartAt = request.StartAt;
        entity.EndAt = request.EndAt;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.Now;

        await ReplacePromotionItemListAsync(id, request);
        await _db.SaveChangesAsync();

        // Sửa đợt KHÔNG đụng tới các lượt đã đổi: giá của chúng đã chốt vào ShopRedemption.UnitPointCost
        // ngay lúc đổi. Nói rõ ra để quản trị viên không tưởng vừa hạ giá cho cả người đã đổi hôm qua.
        return Result.Success(new MessageResponse("Đã cập nhật chương trình. Các lượt đã đổi trước đó giữ nguyên giá cũ."));
    }

    public async Task<Result> DeletePromotion(int id)
    {
        var entity = await _db.ShopPromotion.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy chương trình khuyến mãi."));

        // Xóa được kể cả khi đã có người đổi trong đợt: liên kết ở lượt đổi chuyển thành null còn
        // PromotionName vẫn nằm lại (xem AppDbContext), nên lịch sử vẫn đọc ra tên đợt.
        _db.ShopPromotion.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa chương trình khuyến mãi. Lịch sử các lượt đã đổi vẫn giữ nguyên."));
    }

    /// <summary>Kiểm một chương trình trước khi lưu. Trả null nếu hợp lệ, hoặc câu giải thích để hiện thẳng
    /// cho quản trị viên.</summary>
    private async Task<string?> ValidatePromotionAsync(ShopPromotionRequest request, int? selfId)
    {
        var name = request.Name.Trim();
        if (await _db.ShopPromotion.AnyAsync(x => x.Name == name && x.Id != selfId))
            return "Đã có chương trình khuyến mãi trùng tên này.";

        if (request.EndAt <= request.StartAt)
            return "Ngày kết thúc phải sau ngày bắt đầu.";

        if (request.DiscountType == ShopDiscountType.Percent && request.DiscountValue > 100)
            return "Giảm theo phần trăm chỉ nhận mức từ 1 đến 100.";

        if (request.ScopeType == ShopPromotionScope.Category && string.IsNullOrWhiteSpace(request.ScopeCategory))
            return "Phạm vi theo nhóm sản phẩm cần chọn một nhóm.";

        if (request.ScopeType == ShopPromotionScope.SelectedProduct)
        {
            var idList = request.ProductIdList.Distinct().ToList();
            if (idList.Count == 0)
                return "Phạm vi chọn từng sản phẩm cần tick ít nhất một món.";

            var foundCount = await _db.ShopProduct.CountAsync(x => idList.Contains(x.Id));
            if (foundCount != idList.Count)
                return "Danh sách sản phẩm có món không còn tồn tại. Hãy đóng và mở lại biểu mẫu.";
        }

        return null;
    }

    /// <summary>Ghi lại danh sách món được tick của một đợt: xóa hết rồi thêm lại theo yêu cầu mới.
    ///
    /// Ghi đè trọn gói thay vì so ra phần thêm/bớt vì danh sách này chỉ vài chục dòng, mà so từng phần là
    /// nơi rất dễ để sót một món đã bỏ tick vẫn nằm lại và tiếp tục được giảm giá.</summary>
    private async Task ReplacePromotionItemListAsync(int promotionId, ShopPromotionRequest request)
    {
        var oldList = await _db.ShopPromotionItem.Where(x => x.ShopPromotionId == promotionId).ToListAsync();
        _db.ShopPromotionItem.RemoveRange(oldList);

        // Đổi phạm vi sang "toàn cửa hàng" / "theo nhóm" thì danh sách tick cũ phải biến mất, nếu không nó
        // sống lại nguyên vẹn ở lần đổi phạm vi ngược lại — một trạng thái không ai chọn.
        if (request.ScopeType != ShopPromotionScope.SelectedProduct) return;

        _db.ShopPromotionItem.AddRange(request.ProductIdList.Distinct()
            .Select(productId => new ShopPromotionItem { ShopPromotionId = promotionId, ShopProductId = productId }));
    }

    // ==================== TÍNH GIÁ KHUYẾN MÃI ====================
    // Một chỗ duy nhất quyết định "món này hôm nay bao nhiêu điểm", dùng chung cho cả lúc bày hàng lẫn lúc
    // bấm đổi. Hai chỗ tính riêng là sớm muộn cũng lệch nhau, mà lệch ở đây nghĩa là người học thấy một giá
    // rồi bị trừ một giá khác.

    /// <summary>Một đợt đang chạy, đã nạp sẵn danh sách món được tick.</summary>
    private sealed record ActivePromotion(
        int Id, string Name, string ColorHex, string Icon,
        ShopDiscountType DiscountType, int DiscountValue,
        ShopPromotionScope ScopeType, string? ScopeCategory, HashSet<int> ProductIdSet,
        DateTime StartAt, DateTime EndAt);

    /// <summary>Các đợt đang có hiệu lực NGAY BÂY GIỜ theo đồng hồ máy chủ.</summary>
    private async Task<List<ActivePromotion>> GetRunningPromotionListAsync()
    {
        var now = DateTime.Now;
        var rowList = await _db.ShopPromotion.AsNoTracking()
            .Where(x => x.IsActive && x.StartAt <= now && x.EndAt >= now)
            .Select(x => new
            {
                x.Id, x.Name, x.ColorHex, x.Icon, x.DiscountType, x.DiscountValue,
                x.ScopeType, x.ScopeCategory, x.StartAt, x.EndAt,
                ProductIdList = x.ItemList.Select(i => i.ShopProductId).ToList()
            })
            .ToListAsync();

        return rowList.Select(x => new ActivePromotion(
            x.Id, x.Name, x.ColorHex, x.Icon, x.DiscountType, x.DiscountValue,
            x.ScopeType, x.ScopeCategory, x.ProductIdList.ToHashSet(), x.StartAt, x.EndAt)).ToList();
    }

    /// <summary>Giá phải trả hôm nay cho một sản phẩm, và đợt đã làm nên mức giá đó (null nếu bán giá niêm yết).
    ///
    /// Nhiều đợt cùng phủ một món thì lấy đợt cho giá THẤP NHẤT, bằng nhau thì lấy đợt bắt đầu muộn hơn —
    /// KHÔNG cộng dồn, xem <see cref="ShopPromotion"/>.</summary>
    private static (int Cost, ActivePromotion? Promotion) ApplyBestPromotion(
        int productId, string? category, int pointCost, List<ActivePromotion> promotionList)
    {
        var best = (Cost: pointCost, Promotion: (ActivePromotion?)null);

        foreach (var promotion in promotionList)
        {
            var covered = promotion.ScopeType switch
            {
                ShopPromotionScope.AllProduct => true,
                ShopPromotionScope.Category => category == promotion.ScopeCategory,
                _ => promotion.ProductIdSet.Contains(productId)
            };
            if (!covered) continue;

            var cost = DiscountedCost(pointCost, promotion.DiscountType, promotion.DiscountValue);
            if (cost < best.Cost || (cost == best.Cost && best.Promotion is not null
                                     && promotion.StartAt > best.Promotion.StartAt))
                best = (cost, promotion);
        }

        return best;
    }

    /// <summary>Giá sau khi giảm, luôn tối thiểu 1 điểm — xem <see cref="ShopPromotion.DiscountValue"/>.</summary>
    private static int DiscountedCost(int pointCost, ShopDiscountType type, int value) => Math.Max(1, type switch
    {
        ShopDiscountType.Percent => pointCost - (int)Math.Floor(pointCost * value / 100.0),
        _ => pointCost - value
    });

    private static string DiscountLabel(ShopDiscountType type, int value) =>
        type == ShopDiscountType.Percent ? $"-{value}%" : $"-{value} điểm";

    private static string ScopeLabel(ShopPromotionScope scope, string? category) => scope switch
    {
        ShopPromotionScope.AllProduct => "Toàn bộ cửa hàng",
        ShopPromotionScope.Category => $"Nhóm \"{category}\"",
        _ => "Sản phẩm đã chọn"
    };

    private static string PromotionStatus(bool isActive, DateTime startAt, DateTime endAt, DateTime now) =>
        !isActive ? "Paused"
        : now < startAt ? "Scheduled"
        : now > endAt ? "Ended"
        : "Running";

    private static string PromotionStatusLabel(string status) => status switch
    {
        "Running" => "Đang chạy",
        "Scheduled" => "Sắp diễn ra",
        "Ended" => "Đã kết thúc",
        _ => "Đang tắt"
    };

    // ==================== DÙNG CHUNG ====================

    /// <summary>Quản trị viên, hoặc vai trò do Admin tự tạo được cấp quyền sửa cửa hàng — chỉ dùng để quyết
    /// định có cho xem món đã ngừng bán hay không. Mọi thao tác ghi vẫn chặn bằng [Authorize] riêng.</summary>
    private bool CanManageShop => _currentUser.IsAdmin
        || FunctionKey.Parse(_currentUser.FindClaim(AppClaimType.PermissionClaimType))
            .Contains("shop." + FunctionKey.Update);

    /// <summary>Điểm tích lũy / đã tiêu ở cửa hàng / còn tiêu được của một người.</summary>
    private async Task<(int Total, int Used, int Available)> GetBalanceAsync(int userId)
    {
        var total = await _db.UserPointLog.AsNoTracking()
            .Where(x => x.UserId == userId).SumAsync(x => (int?)x.Points) ?? 0;

        var used = await _db.ShopRedemption.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status != ShopRedemptionStatus.Cancelled)
            .SumAsync(x => (int?)x.TotalPoint) ?? 0;

        // Chặn ở 0 thay vì để lộ số âm: quản trị viên có thể xóa bớt dòng điểm sau khi người học đã tiêu,
        // và "còn -50 điểm" là một câu vô nghĩa với người đọc.
        return (total, used, Math.Max(0, total - used));
    }

    /// <summary>Đọc một tập lượt đổi ra DTO. Lấy dữ liệu thô về trước rồi mới gắn nhãn trạng thái: nhãn
    /// tiếng Việt do <see cref="StatusLabel"/> sinh ra, mà hàm C# thì không dịch được sang SQL — gọi thẳng
    /// trong Select là câu truy vấn ném lỗi ngay lúc chạy.</summary>
    private async Task<List<ShopRedemptionDto>> ToRedemptionListAsync(IQueryable<ShopRedemption> query)
    {
        var rowList = await query.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id, x.UserId, UserName = x.User.FullName, x.User.EmployeeCode,
                DepartmentName = x.User.Department != null ? x.User.Department.Name : null,
                x.User.AvatarUrl,
                x.ShopProductId, x.ProductName, ProductImageUrl = x.ShopProduct.ImageUrl,
                x.Quantity, x.UnitPointCost, x.OriginalPointCost, x.PromotionName,
                x.TotalPoint, x.Status, x.Note,
                x.CreatedAt, x.HandledAt,
                HandledByName = x.HandledBy != null ? x.HandledBy.FullName : null
            })
            .ToListAsync();

        return rowList.Select(x => new ShopRedemptionDto(
            x.Id, x.UserId, x.UserName, x.EmployeeCode, x.DepartmentName, x.AvatarUrl,
            x.ShopProductId, x.ProductName, x.ProductImageUrl,
            x.Quantity, x.UnitPointCost, x.OriginalPointCost,
            Math.Max(0, x.OriginalPointCost - x.UnitPointCost) * x.Quantity, x.PromotionName, x.TotalPoint,
            x.Status.ToString(), StatusLabel(x.Status), x.Note,
            x.CreatedAt, x.HandledAt, x.HandledByName)).ToList();
    }

    private static string StatusLabel(ShopRedemptionStatus status) => status switch
    {
        ShopRedemptionStatus.Pending => "Chờ trao quà",
        ShopRedemptionStatus.Delivered => "Đã trao quà",
        ShopRedemptionStatus.Cancelled => "Đã hủy, hoàn điểm",
        _ => status.ToString()
    };
}
