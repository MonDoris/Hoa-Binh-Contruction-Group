// File: API/Controllers/ShopController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Shop. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IShopService"/>.</summary>
public class ShopController : BaseApiController
{
    private readonly IShopService _service;

    public ShopController(IShopService service) => _service = service;

    [HttpGet("me")]
    public async Task<ActionResult<ShopBalanceDto>> GetMyBalance()
        => ToActionResult(await _service.GetMyBalance());

    [HttpGet("product")]
    public async Task<ActionResult<List<ShopProductDto>>> GetProductList([FromQuery] string? keyword = null, [FromQuery] string? category = null, [FromQuery] bool includeInactive = false)
        => ToActionResult(await _service.GetProductList(keyword, category, includeInactive));

    [HttpPost("product")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> CreateProduct(ShopProductRequest request)
        => ToActionResult(await _service.CreateProduct(request));

    [HttpPut("product/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> UpdateProduct(int id, ShopProductRequest request)
        => ToActionResult(await _service.UpdateProduct(id, request));

    [HttpDelete("product/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> DeleteProduct(int id)
        => ToActionResult(await _service.DeleteProduct(id));

    [HttpPost("product/{id:int}/redeem")]
    public async Task<ActionResult<MessageResponse>> Redeem(int id, RedeemRequest request)
        => ToActionResult(await _service.Redeem(id, request));

    [HttpGet("my-redemption")]
    public async Task<ActionResult<List<ShopRedemptionDto>>> GetMyRedemptionList()
        => ToActionResult(await _service.GetMyRedemptionList());

    [HttpGet("product/{id:int}/redemption")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<ShopRedemptionDto>>> GetProductRedemptionList(int id)
        => ToActionResult(await _service.GetProductRedemptionList(id));

    [HttpGet("redemption")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<ShopRedemptionDto>>> GetRedemptionList([FromQuery] ShopRedemptionStatus? status = null, [FromQuery] string? keyword = null)
        => ToActionResult(await _service.GetRedemptionList(status, keyword));

    [HttpPut("redemption/{id:int}/status")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> UpdateRedemptionStatus(int id, UpdateRedemptionStatusRequest request)
        => ToActionResult(await _service.UpdateRedemptionStatus(id, request));

    [HttpGet("promotion")]
    public async Task<ActionResult<List<ShopPromotionDto>>> GetPromotionList([FromQuery] bool includeAll = false)
        => ToActionResult(await _service.GetPromotionList(includeAll));

    [HttpPost("promotion")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> CreatePromotion(ShopPromotionRequest request)
        => ToActionResult(await _service.CreatePromotion(request));

    [HttpPut("promotion/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> UpdatePromotion(int id, ShopPromotionRequest request)
        => ToActionResult(await _service.UpdatePromotion(id, request));

    [HttpDelete("promotion/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> DeletePromotion(int id)
        => ToActionResult(await _service.DeletePromotion(id));
}
