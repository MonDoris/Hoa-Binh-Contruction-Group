// File: API/Controllers/UploadController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Upload. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IUploadService"/>.</summary>
public class UploadController : BaseApiController
{
    private readonly IUploadService _service;

    public UploadController(IUploadService service) => _service = service;

    [HttpPost]
    [RequestSizeLimit(524_288_000)]
    public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file, [FromQuery] string subFolder = "lesson")
        => ToActionResult(await _service.Upload(file.ToFileUpload(), subFolder));

    [HttpDelete]
    public ActionResult<MessageResponse> Delete([FromQuery] string url)
        => ToActionResult(_service.Delete(url));
}
