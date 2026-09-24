// File: API/Controllers/SimAssetController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng SimAsset. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ISimAssetService"/>.</summary>
[Authorize(Roles = RoleName.AdminOrTrainer)]
public class SimAssetController : BaseApiController
{
    private readonly ISimAssetService _service;

    public SimAssetController(ISimAssetService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SimAssetListItemDto>>> GetList([FromQuery] string? assetType, [FromQuery] string? keyword, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetList(assetType, keyword, cancellationToken));

    [HttpPost]
    [RequestSizeLimit(524_288_000)]
    public async Task<ActionResult<SimAssetListItemDto>> Upload(IFormFile file, [FromForm] string name, [FromForm] string assetType, [FromForm] string? description, [FromForm] string? category, CancellationToken cancellationToken)
        => ToActionResult(await _service.Upload(file.ToFileUpload(), name, assetType, description, category, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Update(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Update(id, request, cancellationToken));

    [HttpPost("{id:int}/thumbnail")]
    public async Task<ActionResult<MessageResponse>> SaveThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.SaveThumbnail(id, request, cancellationToken));

    [HttpPut("builtin/{id:int}")]
    public async Task<ActionResult<MessageResponse>> UpdateBuiltin(int id, SimAssetUpdateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.UpdateBuiltin(id, request, cancellationToken));

    [HttpPost("builtin/{id:int}/thumbnail")]
    public async Task<ActionResult<MessageResponse>> SaveBuiltinThumbnail(int id, SimAssetThumbnailRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.SaveBuiltinThumbnail(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.Delete(id, cancellationToken));
}
