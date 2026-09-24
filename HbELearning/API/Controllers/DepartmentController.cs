// File: API/Controllers/DepartmentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Department. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IDepartmentService"/>.</summary>
public class DepartmentController : BaseApiController
{
    private readonly IDepartmentService _service;

    public DepartmentController(IDepartmentService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentDto>>> GetList([FromQuery] bool? isActive)
        => ToActionResult(await _service.GetList(isActive));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpPost]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Update(int id, DepartmentRequest request)
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
