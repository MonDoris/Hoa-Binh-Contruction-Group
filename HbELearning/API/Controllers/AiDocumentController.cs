// File: API/Controllers/AiDocumentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng AiDocument. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAiDocumentService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class AiDocumentController : BaseApiController
{
    private readonly IAiDocumentService _service;

    public AiDocumentController(IAiDocumentService service) => _service = service;

    [HttpGet("status")]
    public async Task<ActionResult<AiDocumentStatusDto>> GetStatus(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetStatus(cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiDocumentListItemDto>>> GetList([FromQuery] int? courseId, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetList(courseId, cancellationToken));

    [HttpGet("chunk")]
    public async Task<ActionResult<IEnumerable<DocumentChunkMatchDto>>> GetChunkList([FromQuery] string sourceFileName, [FromQuery] int? courseId, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetChunkList(sourceFileName, courseId, cancellationToken));

    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)]
    public async Task<ActionResult<DocumentIngestResultDto>> Upload(IFormFile file, [FromQuery] int? courseId, [FromQuery] int? lessonId, CancellationToken cancellationToken)
        => ToActionResult(await _service.Upload(file.ToFileUpload(), courseId, lessonId, cancellationToken));

    [HttpPost("search")]
    public async Task<ActionResult<IEnumerable<DocumentChunkMatchDto>>> Search(AiDocumentSearchRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Search(request, cancellationToken));

    [HttpPost("ask")]
    public async Task Ask(AiDocumentAskRequest request, CancellationToken cancellationToken)
        => await _service.Ask(request, new ServerSentEventStream(Response), cancellationToken);

    [HttpGet("course-knowledge")]
    public async Task<ActionResult<CourseKnowledgeStatusDto>> GetCourseKnowledge(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetCourseKnowledge(cancellationToken));

    [HttpPost("course-knowledge/sync")]
    public async Task<ActionResult<CourseKnowledgeSyncResultDto>> SyncCourseKnowledge([FromQuery] bool isForced, [FromQuery] int? courseId, CancellationToken cancellationToken)
        => ToActionResult(await _service.SyncCourseKnowledge(isForced, courseId, cancellationToken));

    [HttpDelete]
    public async Task<ActionResult<MessageResponse>> Delete([FromQuery] string sourceFileName, [FromQuery] int? courseId, CancellationToken cancellationToken)
        => ToActionResult(await _service.Delete(sourceFileName, courseId, cancellationToken));
}
