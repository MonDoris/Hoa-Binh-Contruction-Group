// File: API/Controllers/HrSyncController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của việc đồng bộ nhân sự từ phần mềm quản lý nhân sự bên ngoài. Chỉ Admin — đây là việc
/// tạo / sửa / khóa hàng loạt tài khoản. Nghiệp vụ nằm ở <see cref="IHrSyncService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class HrSyncController : BaseApiController
{
    private readonly IHrSyncService _service;

    public HrSyncController(IHrSyncService service) => _service = service;

    [HttpGet("status")]
    public ActionResult GetStatus() => ToActionResult(_service.GetStatus());

    /// <summary>Tính thử sẽ thêm / sửa / khóa bao nhiêu người. Không ghi gì vào hệ thống.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult> Preview(CancellationToken ct) => ToActionResult(await _service.Run(previewOnly: true, ct));

    [HttpPost("run")]
    public async Task<ActionResult> Run(CancellationToken ct) => ToActionResult(await _service.Run(previewOnly: false, ct));
}
