// File: API/Controllers/PositionController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Position. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IPositionService"/>.</summary>
public class PositionController : BaseApiController
{
    private readonly IPositionService _service;

    public PositionController(IPositionService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PositionDto>>> GetList([FromQuery] int? departmentId, [FromQuery] bool? isActive)
        => ToActionResult(await _service.GetList(departmentId, isActive));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PositionDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpPost]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<PositionDto>> Create(PositionRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Update(int id, PositionRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpGet("import/template")]
    [Authorize(Roles = RoleName.Admin)]
    public IActionResult DownloadImportTemplate()
        => ToActionResult(_service.DownloadImportTemplate());

    [HttpPost("import")]
    [Authorize(Roles = RoleName.Admin)]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file)
        => ToActionResult(await _service.Import(file.ToFileUpload()));
}
