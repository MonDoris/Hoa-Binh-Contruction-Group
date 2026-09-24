// File: API/Controllers/CertificateController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Certificate. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ICertificateService"/>.</summary>
public class CertificateController : BaseApiController
{
    private readonly ICertificateService _service;

    public CertificateController(ICertificateService service) => _service = service;

    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<CertificateDto>>> GetMy()
        => ToActionResult(await _service.GetMy());

    [HttpGet]
    [Authorize(Roles = RoleName.AdminOrTrainer)]
    public async Task<ActionResult<IEnumerable<CertificateDto>>> GetList([FromQuery] int? courseId, [FromQuery] int? departmentId)
        => ToActionResult(await _service.GetList(courseId, departmentId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CertificateDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpGet("subordinate")]
    public async Task<ActionResult<IEnumerable<CertificateSubordinateDto>>> GetSubordinateList([FromQuery] string? keyword, [FromQuery] int? departmentId)
        => ToActionResult(await _service.GetSubordinateList(keyword, departmentId));

    [HttpGet("subordinate-department")]
    public async Task<ActionResult<IEnumerable<CertificateDepartmentDto>>> GetSubordinateDepartmentList()
        => ToActionResult(await _service.GetSubordinateDepartmentList());

    [HttpGet("by-user/{userId:int}")]
    public async Task<ActionResult<IEnumerable<CertificateDto>>> GetByUser(int userId)
        => ToActionResult(await _service.GetByUser(userId));

    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<CertificateVerifyDto>> Verify(string code)
        => ToActionResult(await _service.Verify(code));

    [HttpPost("issue/{enrollmentId:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Issue(int enrollmentId)
        => ToActionResult(await _service.Issue(enrollmentId));

    [HttpPost("{id:int}/revoke")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Revoke(int id)
        => ToActionResult(await _service.Revoke(id));
}
