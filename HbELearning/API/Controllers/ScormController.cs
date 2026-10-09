// File: API/Controllers/ScormController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của SCORM (/api/scorm) — nhập gói, phiên học (RTE + sequencing), báo cáo, xuất gói.
/// Nghiệp vụ nằm ở <see cref="IScormService"/>. Tệp nội dung của gói KHÔNG đi qua đây mà được phục vụ tĩnh ở
/// /scorm-content (xem WebApplicationExtensions).</summary>
public class ScormController : BaseApiController
{
    /// <summary>Trần cho cả yêu cầu tải gói: gói tối đa 500 MB + phần các trường chữ.</summary>
    private const long UploadRequestLimit = ScormService.MaxPackageByte + 1024 * 1024;

    private readonly IScormService _service;

    public ScormController(IScormService service) => _service = service;

    // ==================== GÓI ====================

    [HttpGet("course/{courseId:int}/package")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<IEnumerable<ScormPackageDto>>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    [HttpGet("package/{id:int}")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<ScormPackageDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    /// <summary>Thêm gói SCORM (.zip) vào khóa học có sẵn — thành một bước của lộ trình.</summary>
    [HttpPost("course/{courseId:int}/package")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<ActionResult<MessageResponse>> ImportToCourse(int courseId, [FromForm] ScormPackageImportRequest request, IFormFile? file)
        => ToActionResult(await _service.ImportToCourse(courseId, request, file?.ToFileUpload()));

    /// <summary>Nhập gói SCORM thành NGUYÊN một khóa học mới.</summary>
    [HttpPost("import-course")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<ActionResult<MessageResponse>> ImportAsCourse([FromForm] ScormCourseImportRequest request, IFormFile? file)
        => ToActionResult(await _service.ImportAsCourse(request, file?.ToFileUpload()));

    [HttpPut("package/{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, ScormPackageUpdateRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("package/{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpGet("package/{id:int}/download")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult> DownloadPackage(int id)
        => ToActionResult(await _service.DownloadPackage(id));

    // ==================== PHIÊN HỌC ====================

    /// <summary>Mở (hoặc học tiếp) khóa học — chính là gói SCORM của khóa học: trả hoạt động được giao, dữ liệu khởi tạo
    /// CMI và trạng thái điều hướng. Người soạn mở bằng CourseId (xem thử khóa học) hoặc PackageId (xem thử một gói).</summary>
    [HttpPost("launch")]
    public async Task<ActionResult<ScormSessionDto>> Launch(ScormLaunchRequest request)
        => ToActionResult(await _service.Launch(request));

    [HttpPost("registration/{id:int}/commit")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<ScormCommitResultDto>> Commit(int id, ScormCommitRequest request)
        => ToActionResult(await _service.Commit(id, request));

    /// <summary>Kết thúc SCO đang mở (nếu có) rồi xử lý một yêu cầu điều hướng — Tiếp, Trước, chọn mục, Thoát,
    /// Lưu và thoát... Trình phát cũng gọi điểm này (keepalive) lúc đóng cửa sổ.</summary>
    [HttpPost("registration/{id:int}/navigate")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<ScormSessionDto>> Navigate(int id, ScormNavigateRequest request)
        => ToActionResult(await _service.Navigate(id, request));

    // ==================== BÁO CÁO ====================

    [HttpGet("course/{courseId:int}/report")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<IEnumerable<ScormReportRowDto>>> GetReport(int courseId)
        => ToActionResult(await _service.GetReport(courseId));

    [HttpGet("course/{courseId:int}/report.csv")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult> ExportReportCsv(int courseId)
        => ToActionResult(await _service.ExportReportCsv(courseId));

    [HttpGet("registration/{id:int}")]
    public async Task<ActionResult<ScormRegistrationDetailDto>> GetRegistrationDetail(int id)
        => ToActionResult(await _service.GetRegistrationDetail(id));

    // ==================== XUẤT GÓI ====================

    [HttpGet("course/{courseId:int}/export-preview")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<ScormExportPreviewDto>> GetExportPreview(int courseId)
        => ToActionResult(await _service.GetExportPreview(courseId));

    [HttpGet("course/{courseId:int}/manifest")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult> GetManifest(int courseId)
        => ToActionResult(await _service.GetManifest(courseId));

    [HttpGet("course/{courseId:int}/export")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult> ExportCourse(int courseId)
        => ToActionResult(await _service.ExportCourse(courseId));
}
