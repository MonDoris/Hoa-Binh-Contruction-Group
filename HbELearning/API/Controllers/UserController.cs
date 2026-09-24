// File: API/Controllers/UserController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng User. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IUserService"/>.</summary>
[Authorize(Roles = RoleName.AdminOrTrainer)]
public class UserController : BaseApiController
{
    private readonly IUserService _service;

    public UserController(IUserService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserListDto>>> GetList([FromQuery] string? keyword, [FromQuery] int? departmentId, [FromQuery] int? positionId, [FromQuery] int? roleId, [FromQuery] bool? isActive, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => ToActionResult(await _service.GetList(keyword, departmentId, positionId, roleId, isActive, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserProfileDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpPost]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<UserProfileDto>> Create(UserCreateRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Update(int id, UserUpdateRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Deactivate(int id)
        => ToActionResult(await _service.Deactivate(id));

    [HttpDelete("{id:int}/permanent")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpPost("{id:int}/reset-password")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> ResetPassword(int id, AdminResetPasswordRequest? request)
        => ToActionResult(await _service.ResetPassword(id, request));

    [HttpPost("{id:int}/unlock")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Unlock(int id)
        => ToActionResult(await _service.Unlock(id));

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
