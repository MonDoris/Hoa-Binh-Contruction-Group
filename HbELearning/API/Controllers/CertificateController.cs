// File: API/Controllers/CertificateController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    /// <summary>Tra cứu chứng nhận bằng mã in trên bằng hoặc mã trong QR. Mở cho người ngoài (nhà tuyển dụng,
    /// đối tác) nên có giới hạn tần suất: phản hồi mang tên và mã nhân viên, mà số hiệu chứng nhận thì đánh
    /// liên tiếp, không chặn thì quét một vòng là lấy được danh sách nhân sự kèm khóa đã học.</summary>
    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceExtensions.PublicRateLimitPolicy)]
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

    /// <summary>Danh sách chứng nhận để in (nội bộ + bên ngoài đã xác nhận). source: internal | external | bỏ trống.</summary>
    [HttpGet("register")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<IEnumerable<CertificateRegisterRowDto>>> GetRegister(
        [FromQuery] string? source, [FromQuery] int? departmentId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? keyword)
        => ToActionResult(await _service.GetRegister(source, departmentId, from, to, keyword));
}
