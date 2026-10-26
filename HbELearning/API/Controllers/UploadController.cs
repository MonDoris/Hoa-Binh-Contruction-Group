// File: API/Controllers/UploadController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Upload. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IUploadService"/>.</summary>
public class UploadController : BaseApiController
{
    private readonly IUploadService _service;

    /// <summary>Trần cho cả yêu cầu tải lên: một tệp tối đa 2 GB (= Upload:MaxFileSizeMb mặc định, video bài học
    /// 1080p dễ vượt 500 MB) cộng phần các trường chữ. Đây là trần ở tầng HTTP; trần của riêng nghiệp vụ nằm ở
    /// FileService. Nâng Upload:MaxFileSizeMb quá 2048 thì phải nâng cả hằng số này, không thì yêu cầu bị chặn
    /// từ cửa với thông báo "Request body too large" trước khi mã nghiệp vụ kịp chạy.</summary>
    public const long MaxRequestByte = 2048L * 1024 * 1024 + 1024 * 1024;

    public UploadController(IUploadService service) => _service = service;

    [HttpPost]
    [RequestSizeLimit(MaxRequestByte)]
    // Giới hạn thân form (mặc định chung của cả ứng dụng là 500 MB, xem ApiServiceExtensions) là một cái cửa RIÊNG
    // với RequestSizeLimit: nâng một mà quên cái kia thì tệp lớn vẫn bị chặn.
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestByte)]
    public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file, [FromQuery] string subFolder = "lesson")
        => ToActionResult(await _service.Upload(file.ToFileUpload(), subFolder));

    [HttpDelete]
    public ActionResult<MessageResponse> Delete([FromQuery] string url)
        => ToActionResult(_service.Delete(url));
}
