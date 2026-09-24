// File: API/Controllers/RoleController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Role. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IRoleService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class RoleController : BaseApiController
{
    private readonly IRoleService _service;

    public RoleController(IRoleService service) => _service = service;

    [HttpGet("function")]
    public ActionResult<IEnumerable<FunctionDto>> GetFunction()
        => ToActionResult(_service.GetFunction());

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleListDto>>> GetList()
        => ToActionResult(await _service.GetList());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleListDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpPost]
    public async Task<ActionResult<RoleListDto>> Create(RoleSaveRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Update(int id, RoleSaveRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
