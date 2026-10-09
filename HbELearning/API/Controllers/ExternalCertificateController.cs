// File: API/Controllers/ExternalCertificateController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chứng nhận bên ngoài (/api/externalcertificate) — nghiệp vụ nằm ở
/// <see cref="IExternalCertificateService"/>.</summary>
public class ExternalCertificateController : BaseApiController
{
    private readonly IExternalCertificateService _service;

    public ExternalCertificateController(IExternalCertificateService service) => _service = service;

    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<ExternalCertificateDto>>> GetMy()
        => ToActionResult(await _service.GetMy());

    [HttpGet]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<IEnumerable<ExternalCertificateDto>>> GetList(
        [FromQuery] ExternalCertificateStatus? status, [FromQuery] int? departmentId, [FromQuery] string? keyword)
        => ToActionResult(await _service.GetList(status, departmentId, keyword));

    [HttpGet("pending-count")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult> GetPendingCount()
        => ToActionResult(await _service.GetPendingCount());

    /// <summary>AI đọc giấy vừa chụp / chọn để điền sẵn biểu mẫu — chưa lưu gì. Trên máy chủ AI chạy CPU một lượt
    /// đọc mất cỡ một phút; người dùng đóng hộp thoại / đổi ảnh thì trình duyệt hủy request và lượt đọc dừng theo.</summary>
    [HttpPost("scan")]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<ExternalCertificateScanDto>> Scan(IFormFile? file, CancellationToken cancellationToken)
        => ToActionResult(await _service.Scan(file?.ToFileUpload(), cancellationToken));

    /// <summary>Gửi một chứng nhận: các trường thông tin + một tệp (ảnh chụp camera hoặc PDF) trong cùng form
    /// multipart. Trần 20 MB cho cả request — tệp tối đa 15 MB, phần dư cho các trường chữ.</summary>
    [HttpPost]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<MessageResponse>> Create([FromForm] ExternalCertificateRequest request, IFormFile? file)
        => ToActionResult(await _service.Create(request, file?.ToFileUpload()));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    /// <summary>Tệp quét của chứng nhận — chỉ chủ giấy và Admin. Giao diện tải về bằng fetch có kèm token rồi
    /// hiện qua blob URL, vì thẻ &lt;img&gt;/&lt;iframe&gt; không gửi được header Authorization.</summary>
    [HttpGet("{id:int}/file")]
    public async Task<ActionResult> GetFile(int id)
        => ToActionResult(await _service.GetFile(id));

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Approve(int id)
        => ToActionResult(await _service.Approve(id));

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Reject(int id, ExternalCertificateRejectRequest request)
        => ToActionResult(await _service.Reject(id, request));
}
