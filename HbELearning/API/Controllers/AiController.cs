// File: API/Controllers/AiController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Ai. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAiService"/>.</summary>
[Authorize(Roles = RoleName.ContentAuthor)]
public class AiController : BaseApiController
{
    private readonly IAiService _service;

    public AiController(IAiService service) => _service = service;

    [HttpGet("knowledge-scope")]
    public async Task<ActionResult<AiKnowledgeScopeDto>> GetKnowledgeScope([FromQuery] int? courseId, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetKnowledgeScope(courseId, cancellationToken));

    [HttpPost("lesson-content")]
    public async Task<ActionResult<GenerateLessonContentResponse>> GenerateLessonContent(GenerateLessonContentRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.GenerateLessonContent(request, cancellationToken));

    [HttpPost("lesson-content-stream")]
    public async Task GenerateLessonContentStream(GenerateLessonContentRequest request, CancellationToken cancellationToken)
        => await _service.GenerateLessonContentStream(request, new ServerSentEventStream(Response), cancellationToken);

    [HttpPost("quiz-questions")]
    public async Task<ActionResult<GenerateQuizQuestionsResponse>> GenerateQuizQuestions(GenerateQuizQuestionsRequest request)
        => ToActionResult(await _service.GenerateQuizQuestions(request));
}
